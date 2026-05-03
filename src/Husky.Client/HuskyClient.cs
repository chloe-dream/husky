using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MessageEnvelope>> pendingReplies = new();

    private Task? senderLoopTask;
    private Task? receiverLoopTask;
    private Func<ShutdownReason, CancellationToken, Task>? shutdownHandler;
    private Func<HealthStatus> healthProvider = () => HealthStatus.Healthy;
    private volatile bool isShuttingDown;
    private bool disposed;
    private HuskyUpdateMode updateMode;

    public string? AppName { get; }

    public CancellationToken ShutdownToken => shutdownCts.Token;

    /// <summary>
    /// Capabilities advertised by the launcher in the welcome handshake. Empty
    /// when the launcher pre-dates capability negotiation. Use
    /// <see cref="SupportsManualUpdates"/> for the convenient gate.
    /// </summary>
    public IReadOnlyList<string> LauncherCapabilities { get; private set; } = [];

    public bool SupportsManualUpdates =>
        LauncherCapabilities.Contains(Protocol.Capabilities.ManualUpdates);

    /// <summary>
    /// The update mode the launcher is operating under. Initialised from the
    /// options passed to attach; updated by <see cref="SetUpdateModeAsync"/>.
    /// Always reads <see cref="HuskyUpdateMode.Auto"/> when the launcher does
    /// not advertise the manual-updates capability.
    /// </summary>
    public HuskyUpdateMode UpdateMode => SupportsManualUpdates ? updateMode : HuskyUpdateMode.Auto;

    /// <summary>
    /// Raised when the launcher pushes an unsolicited <c>update-available</c>
    /// notification (manual mode only). The handler is invoked from the
    /// receiver loop — keep it short or marshal to your UI thread.
    /// </summary>
    public event EventHandler<HuskyUpdateInfo>? UpdateAvailable;

    private HuskyClient(Stream pipe, MessageReader reader, MessageWriter writer, string? appName, HuskyClientOptions options)
    {
        this.pipe = pipe;
        this.reader = reader;
        this.writer = writer;
        this.options = options;
        updateMode = options.UpdateMode;
        AppName = appName;
    }

    public static bool IsHosted =>
        Environment.GetEnvironmentVariable(HuskyEnvironment.PipeNameVariable) is { Length: > 0 };

    public static async Task<HuskyClient?> AttachIfHostedAsync(
        HuskyClientOptions? options = null,
        CancellationToken ct = default)
    {
        if (!IsHosted) return null;
        return await AttachAsync(options, ct).ConfigureAwait(false);
    }

    public static async Task<HuskyClient> AttachAsync(
        HuskyClientOptions? options = null,
        CancellationToken ct = default)
    {
        string pipeName = Environment.GetEnvironmentVariable(HuskyEnvironment.PipeNameVariable)
            ?? throw new InvalidOperationException(
                $"Husky is not hosting this app: {HuskyEnvironment.PipeNameVariable} is not set.");
        string? appName = Environment.GetEnvironmentVariable(HuskyEnvironment.AppNameVariable);

        HuskyClientOptions resolved = options ?? HuskyClientOptions.Default;

        NamedPipeClientStream stream = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(resolved.ConnectTimeout);
            await stream.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            return await AttachOnStreamAsync(stream, appName, resolved, ct).ConfigureAwait(false);
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
            await SendHelloAsync(writer, resolvedAppName, options, ct).ConfigureAwait(false);
            WelcomePayload welcome = await ReceiveWelcomeAsync(reader, options.WelcomeTimeout, ct).ConfigureAwait(false);

            HuskyClient client = new(pipe, reader, writer, resolvedAppName, options)
            {
                LauncherCapabilities = welcome.Capabilities ?? [],
            };
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

    public async Task<HuskyUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        EnsureManualUpdatesSupported();

        string id = NewMessageId();
        MessageEnvelope request = new() { Id = id, Type = MessageTypes.UpdateCheck };
        MessageEnvelope reply = await RequestReplyAsync(request, ct).ConfigureAwait(false);

        if (reply.Type != MessageTypes.UpdateStatus)
            throw new InvalidOperationException(
                $"Expected '{MessageTypes.UpdateStatus}' reply to '{MessageTypes.UpdateCheck}'; got '{reply.Type}'.");

        UpdateStatusPayload? payload = reply.Data?.Deserialize(HuskyJsonContext.Default.UpdateStatusPayload);
        if (payload is null) return null;
        if (!payload.Available || payload.NewVersion is null) return null;

        return new HuskyUpdateInfo(
            CurrentVersion: payload.CurrentVersion,
            NewVersion: payload.NewVersion,
            DownloadSizeBytes: payload.DownloadSizeBytes);
    }

    public async Task RequestUpdateAsync(CancellationToken ct = default)
    {
        EnsureManualUpdatesSupported();

        MessageEnvelope envelope = new() { Type = MessageTypes.UpdateNow };
        await WriteSerializedAsync(envelope, ct).ConfigureAwait(false);
    }

    public async Task SetUpdateModeAsync(HuskyUpdateMode mode, CancellationToken ct = default)
    {
        EnsureManualUpdatesSupported();

        SetUpdateModePayload payload = new(Mode: ToWireMode(mode));
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.SetUpdateModePayload);
        string id = NewMessageId();
        MessageEnvelope request = new()
        {
            Id = id,
            Type = MessageTypes.SetUpdateMode,
            Data = data,
        };
        MessageEnvelope reply = await RequestReplyAsync(request, ct).ConfigureAwait(false);

        if (reply.Type != MessageTypes.UpdateModeAck)
            throw new InvalidOperationException(
                $"Expected '{MessageTypes.UpdateModeAck}' reply to '{MessageTypes.SetUpdateMode}'; got '{reply.Type}'.");

        UpdateModeAckPayload? ack = reply.Data?.Deserialize(HuskyJsonContext.Default.UpdateModeAckPayload);
        if (ack is null)
            throw new InvalidOperationException("update-mode-ack payload was missing.");

        updateMode = FromWireMode(ack.Mode);
    }

    private void EnsureManualUpdatesSupported()
    {
        if (!SupportsManualUpdates)
            throw new NotSupportedException(
                $"The Husky launcher does not advertise the '{Protocol.Capabilities.ManualUpdates}' capability.");
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

                // Reply correlation runs first — any envelope carrying a replyTo
                // matches a pending request regardless of message type.
                if (envelope.ReplyTo is { Length: > 0 } replyTo
                    && pendingReplies.TryRemove(replyTo, out TaskCompletionSource<MessageEnvelope>? tcs))
                {
                    tcs.TrySetResult(envelope);
                    continue;
                }

                switch (envelope.Type)
                {
                    case MessageTypes.Shutdown:
                        await HandleShutdownMessageAsync(envelope, ct).ConfigureAwait(false);
                        break;
                    case MessageTypes.Ping:
                        await HandlePingAsync(envelope, ct).ConfigureAwait(false);
                        break;
                    case MessageTypes.UpdateAvailable:
                        HandleUpdateAvailable(envelope);
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
        FailAllPendingReplies(new IOException("Husky pipe closed."));
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

    private void HandleUpdateAvailable(MessageEnvelope envelope)
    {
        UpdateAvailablePayload? payload = envelope.Data?.Deserialize(HuskyJsonContext.Default.UpdateAvailablePayload);
        if (payload is null) return;

        EventHandler<HuskyUpdateInfo>? handler = UpdateAvailable;
        if (handler is null) return;

        HuskyUpdateInfo info = new(
            CurrentVersion: payload.CurrentVersion,
            NewVersion: payload.NewVersion,
            DownloadSizeBytes: payload.DownloadSizeBytes);

        try { handler(this, info); }
        catch { /* event handler exceptions don't take down the pipe loop */ }
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

    private async Task<MessageEnvelope> RequestReplyAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Id is not { Length: > 0 } id)
            throw new InvalidOperationException("Request envelope must have an id.");

        TaskCompletionSource<MessageEnvelope> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingReplies.TryAdd(id, tcs))
            throw new InvalidOperationException($"Duplicate request id: {id}");

        using CancellationTokenSource timeoutCts = new(options.RequestReplyTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token, lifetimeCts.Token);
        await using CancellationTokenRegistration registration = linked.Token.Register(static state =>
        {
            ((TaskCompletionSource<MessageEnvelope>)state!).TrySetCanceled();
        }, tcs);

        try
        {
            await WriteSerializedAsync(envelope, ct).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Husky launcher did not reply to '{envelope.Type}' within {options.RequestReplyTimeout.TotalSeconds:F0}s.");
        }
        finally
        {
            pendingReplies.TryRemove(id, out _);
        }
    }

    private void FailAllPendingReplies(Exception cause)
    {
        foreach (KeyValuePair<string, TaskCompletionSource<MessageEnvelope>> entry in pendingReplies)
        {
            entry.Value.TrySetException(cause);
        }
        pendingReplies.Clear();
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

    private static string NewMessageId() => Guid.NewGuid().ToString("D");

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

    private static string ToWireMode(HuskyUpdateMode mode) => mode switch
    {
        HuskyUpdateMode.Manual => UpdateModes.Manual,
        _ => UpdateModes.Auto,
    };

    private static HuskyUpdateMode FromWireMode(string wire) => wire switch
    {
        UpdateModes.Manual => HuskyUpdateMode.Manual,
        _ => HuskyUpdateMode.Auto,
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

    private static async Task SendHelloAsync(MessageWriter writer, string appName, HuskyClientOptions options, CancellationToken ct)
    {
        HelloPayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            AppVersion: GetAppVersion(),
            AppName: appName,
            Pid: Environment.ProcessId,
            Capabilities: [Protocol.Capabilities.ManualUpdates, Protocol.Capabilities.ShutdownProgress],
            Preferences: new HelloPreferences(UpdateMode: ToWireMode(options.UpdateMode)));

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.HelloPayload);
        MessageEnvelope envelope = new()
        {
            Id = NewMessageId(),
            Type = MessageTypes.Hello,
            Data = data,
        };

        await writer.WriteAsync(envelope, ct).ConfigureAwait(false);
    }

    private static async Task<WelcomePayload> ReceiveWelcomeAsync(MessageReader reader, TimeSpan timeout, CancellationToken ct)
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

        return welcome;
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

        FailAllPendingReplies(new ObjectDisposedException(nameof(HuskyClient)));

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
