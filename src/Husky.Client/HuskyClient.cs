using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Husky.Protocol;

namespace Husky.Client;

public sealed class HuskyClient : IAsyncDisposable
{
    private readonly Stream pipe;
    private readonly MessageReader reader;
    private readonly MessageWriter writer;
    private readonly HuskyClientOptions options;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly CancellationTokenSource shutdownCts = new();
    private readonly SemaphoreSlim writeMutex = new(initialCount: 1, maxCount: 1);

    private Task? senderLoopTask;
    private Task? receiverLoopTask;
    private Func<ShutdownReason, CancellationToken, Task>? shutdownHandler;
    private Func<HealthStatus> healthProvider = () => HealthStatus.Healthy;
    private volatile bool isShuttingDown;
    private bool disposed;

    public string? AppName { get; }

    public CancellationToken ShutdownToken => shutdownCts.Token;

    private HuskyClient(Stream pipe, MessageReader reader, MessageWriter writer, string? appName, HuskyClientOptions options)
    {
        this.pipe = pipe;
        this.reader = reader;
        this.writer = writer;
        this.options = options;
        AppName = appName;
    }

    public static bool IsHosted =>
        Environment.GetEnvironmentVariable(HuskyEnvironment.PipeNameVariable) is { Length: > 0 };

    public static async Task<HuskyClient?> AttachIfHostedAsync(CancellationToken ct = default)
    {
        if (!IsHosted) return null;
        return await AttachAsync(ct).ConfigureAwait(false);
    }

    public static async Task<HuskyClient> AttachAsync(CancellationToken ct = default)
    {
        string pipeName = Environment.GetEnvironmentVariable(HuskyEnvironment.PipeNameVariable)
            ?? throw new InvalidOperationException(
                $"Husky is not hosting this app: {HuskyEnvironment.PipeNameVariable} is not set.");
        string? appName = Environment.GetEnvironmentVariable(HuskyEnvironment.AppNameVariable);

        HuskyClientOptions options = HuskyClientOptions.Default;

        NamedPipeClientStream stream = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(options.ConnectTimeout);
            await stream.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            return await AttachOnStreamAsync(stream, appName, options, ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<HuskyClient> AttachOnStreamAsync(
        Stream pipe,
        string? appName,
        HuskyClientOptions options,
        CancellationToken ct)
    {
        MessageReader reader = new(pipe, leaveOpen: true);
        MessageWriter writer = new(pipe, leaveOpen: true);

        try
        {
            string resolvedAppName = appName ?? GetAppNameFallback();
            await SendHelloAsync(writer, resolvedAppName, ct).ConfigureAwait(false);
            await ReceiveWelcomeAsync(reader, options.WelcomeTimeout, ct).ConfigureAwait(false);

            HuskyClient client = new(pipe, reader, writer, resolvedAppName, options);
            client.StartLoops();
            return client;
        }
        catch
        {
            reader.Dispose();
            await writer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void OnShutdown(Func<ShutdownReason, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        shutdownHandler = handler;
    }

    public void SetHealth(Func<HealthStatus> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        healthProvider = provider;
    }

    private void StartLoops()
    {
        senderLoopTask = Task.Run(() => SenderLoopAsync(lifetimeCts.Token));
        receiverLoopTask = Task.Run(() => ReceiverLoopAsync(lifetimeCts.Token));
    }

    private async Task SenderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(options.HeartbeatInterval, ct).ConfigureAwait(false);
                if (isShuttingDown) continue;

                try
                {
                    MessageEnvelope heartbeat = new() { Type = MessageTypes.Heartbeat };
                    await WriteSerializedAsync(heartbeat, ct).ConfigureAwait(false);
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // Pipe died — receiver loop will surface this via ReadAsync.
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private async Task ReceiverLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                MessageEnvelope? envelope;
                try
                {
                    envelope = await reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (IOException) when (!ct.IsCancellationRequested)
                {
                    envelope = null;
                }

                if (envelope is null)
                {
                    await HandleDisconnectAsync().ConfigureAwait(false);
                    return;
                }

                switch (envelope.Type)
                {
                    case MessageTypes.Shutdown:
                        await HandleShutdownMessageAsync(envelope, ct).ConfigureAwait(false);
                        break;
                    case MessageTypes.Ping:
                        await HandlePingAsync(envelope, ct).ConfigureAwait(false);
                        break;
                    default:
                        // Additive-fields rule §3.6 — unknown message types are dropped.
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private async Task HandleShutdownMessageAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        isShuttingDown = true;

        ShutdownPayload? payload = envelope.Data?.Deserialize(HuskyJsonContext.Default.ShutdownPayload);
        int timeoutSeconds = payload?.TimeoutSeconds ?? 60;
        ShutdownReason reason = ParseShutdownReason(payload?.Reason);

        // 1. shutdown-ack — best-effort.
        MessageEnvelope ack = new() { ReplyTo = envelope.Id, Type = MessageTypes.ShutdownAck };
        try
        {
            await WriteSerializedAsync(ack, ct).ConfigureAwait(false);
        }
        catch when (!ct.IsCancellationRequested) { /* pipe might be gone */ }

        // 2. Invoke handler with a CT that fires (timeoutSeconds - 5) before the launcher's hard-kill.
        TimeSpan handlerWindow = TimeSpan.FromSeconds(Math.Max(timeoutSeconds - 5, 0));
        using CancellationTokenSource handlerCts = new(handlerWindow);
        await InvokeShutdownHandlerAsync(reason, handlerCts.Token).ConfigureAwait(false);

        // 3. Public ShutdownToken.
        SignalShutdown();
    }

    private async Task HandleDisconnectAsync()
    {
        isShuttingDown = true;
        await InvokeShutdownHandlerAsync(ShutdownReason.LauncherStopping, CancellationToken.None).ConfigureAwait(false);
        SignalShutdown();
    }

    private async Task HandlePingAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        HealthStatus status;
        try { status = healthProvider(); }
        catch { status = HealthStatus.Healthy; }

        PongPayload payload = new(
            Status: HealthStateToWire(status.State),
            Details: ConvertDetails(status.Details));

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.PongPayload);
        MessageEnvelope pong = new()
        {
            ReplyTo = envelope.Id,
            Type = MessageTypes.Pong,
            Data = data,
        };

        try
        {
            await WriteSerializedAsync(pong, ct).ConfigureAwait(false);
        }
        catch when (!ct.IsCancellationRequested) { /* pipe might be gone */ }
    }

    private async Task InvokeShutdownHandlerAsync(ShutdownReason reason, CancellationToken handlerCt)
    {
        if (shutdownHandler is not { } handler) return;

        try
        {
            await handler(reason, handlerCt).ConfigureAwait(false);
        }
        catch
        {
            // Handler exceptions are not propagated — the launcher will hard-kill if needed.
        }
    }

    private void SignalShutdown()
    {
        try { shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed */ }
    }

    private async Task WriteSerializedAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        await writeMutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await writer.WriteAsync(envelope, ct).ConfigureAwait(false);
        }
        finally
        {
            writeMutex.Release();
        }
    }

    private static ShutdownReason ParseShutdownReason(string? reason) => reason switch
    {
        "update" => ShutdownReason.Update,
        "manual" => ShutdownReason.Manual,
        "launcher-stopping" => ShutdownReason.LauncherStopping,
        _ => ShutdownReason.Manual,
    };

    private static string HealthStateToWire(HealthState state) => state switch
    {
        HealthState.Healthy => "healthy",
        HealthState.Degraded => "degraded",
        HealthState.Unhealthy => "unhealthy",
        _ => "healthy",
    };

    private static Dictionary<string, JsonElement>? ConvertDetails(
        IReadOnlyDictionary<string, object>? details)
    {
        if (details is null) return null;

        Dictionary<string, JsonElement> result = new(details.Count);
        foreach (KeyValuePair<string, object> entry in details)
        {
            result[entry.Key] = JsonSerializer.SerializeToElement(entry.Value);
        }
        return result;
    }

    private static async Task SendHelloAsync(MessageWriter writer, string appName, CancellationToken ct)
    {
        HelloPayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            AppVersion: GetAppVersion(),
            AppName: appName,
            Pid: Environment.ProcessId);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.HelloPayload);
        MessageEnvelope envelope = new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Type = MessageTypes.Hello,
            Data = data,
        };

        await writer.WriteAsync(envelope, ct).ConfigureAwait(false);
    }

    private static async Task ReceiveWelcomeAsync(MessageReader reader, TimeSpan timeout, CancellationToken ct)
    {
        using CancellationTokenSource welcomeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        welcomeCts.CancelAfter(timeout);

        MessageEnvelope? envelope;
        try
        {
            envelope = await reader.ReadAsync(welcomeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Husky launcher did not send a welcome within {timeout.TotalSeconds:F0}s.");
        }

        if (envelope is null)
            throw new IOException("Husky pipe was closed before a welcome arrived.");

        if (envelope.Type != MessageTypes.Welcome)
            throw new InvalidOperationException(
                $"Expected '{MessageTypes.Welcome}' from Husky launcher; got '{envelope.Type}'.");

        WelcomePayload? welcome = envelope.Data?.Deserialize(HuskyJsonContext.Default.WelcomePayload);
        if (welcome is null)
            throw new InvalidOperationException("Husky welcome message is missing its payload.");

        if (!welcome.Accepted)
            throw new InvalidOperationException(
                $"Husky launcher refused this app: {welcome.Reason ?? "(no reason given)"}");
    }

    private static string GetAppVersion()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        if (entry is null) return "0.0.0";

        string? informational = entry
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational;

        return entry.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string GetAppNameFallback()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        return entry?.GetName().Name ?? "unknown";
    }

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        return DisposeCoreAsync();
    }

    private async ValueTask DisposeCoreAsync()
    {
        try { lifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }

        Task pendingLoops = Task.WhenAll(
            senderLoopTask ?? Task.CompletedTask,
            receiverLoopTask ?? Task.CompletedTask);

        try
        {
            await pendingLoops.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException) { /* loops won't drain — proceed with cleanup */ }
        catch (OperationCanceledException) { /* normal */ }

        reader.Dispose();
        await writer.DisposeAsync().ConfigureAwait(false);
        await pipe.DisposeAsync().ConfigureAwait(false);

        writeMutex.Dispose();
        lifetimeCts.Dispose();
        shutdownCts.Dispose();
    }
}
