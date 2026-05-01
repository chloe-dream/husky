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
    private bool disposed;

    public string? AppName { get; }

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
            return new HuskyClient(pipe, reader, writer, resolvedAppName, options);
        }
        catch
        {
            reader.Dispose();
            await writer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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
        reader.Dispose();
        await writer.DisposeAsync().ConfigureAwait(false);
        await pipe.DisposeAsync().ConfigureAwait(false);
    }
}
