using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Husky.Protocol;

namespace Husky;

internal sealed class AppPipeServer : IAsyncDisposable
{
    private const int PipeBufferSize = 65_536;

    private readonly NamedPipeServerStream pipe;
    private readonly MessageReader reader;
    private readonly MessageWriter writer;
    private readonly SemaphoreSlim writeMutex = new(initialCount: 1, maxCount: 1);
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> pendingPings = new();
    private Task? receiverLoop;
    private TaskCompletionSource? shutdownAckTcs;
    private string? shutdownAckExpectedFor;
    private bool disposed;

    public string PipeName { get; }
    public string LauncherVersion { get; }
    public ConnectedApp? ConnectedApp { get; private set; }

    /// <summary>
    /// Fires once for every message received from the hosted app — heartbeat,
    /// pong, shutdown-ack, or unknown — *before* type-specific dispatch. The
    /// watchdog uses this to refresh its <c>lastActivity</c> timestamp per
    /// LEASH §8.1.
    /// </summary>
    public Action? OnActivity { get; set; }

    internal Task? ReceiverTask => receiverLoop;

    private AppPipeServer(string pipeName, NamedPipeServerStream pipe, string launcherVersion)
    {
        PipeName = pipeName;
        this.pipe = pipe;
        LauncherVersion = launcherVersion;
        reader = new MessageReader(pipe, leaveOpen: true);
        writer = new MessageWriter(pipe, leaveOpen: true);
    }

    public static AppPipeServer Create(string pipeName, string launcherVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);

        NamedPipeServerStream pipe = CreatePipe(pipeName);
        return new AppPipeServer(pipeName, pipe, launcherVersion);
    }

    public async Task AcceptAndHandshakeAsync(TimeSpan connectTimeout, CancellationToken ct = default)
    {
        if (receiverLoop is not null)
            throw new InvalidOperationException("Handshake has already been performed.");

        await WaitForConnectionAsync(connectTimeout, ct).ConfigureAwait(false);

        MessageEnvelope helloEnvelope = await ReadHelloAsync(ct).ConfigureAwait(false);
        HelloPayload hello = ParseHelloPayload(helloEnvelope);

        if (hello.ProtocolVersion != ProtocolVersion.Current)
        {
            string reason =
                $"protocol version mismatch (got {hello.ProtocolVersion}, expected {ProtocolVersion.Current})";
            await SendWelcomeAsync(helloEnvelope.Id, accepted: false, reason: reason, ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Refused hosted app: {reason}.");
        }

        await SendWelcomeAsync(helloEnvelope.Id, accepted: true, reason: null, ct).ConfigureAwait(false);

        ConnectedApp = new ConnectedApp(hello.AppName, hello.AppVersion, hello.Pid);
        receiverLoop = Task.Run(() => ReceiverLoopAsync(lifetimeCts.Token), CancellationToken.None);
    }

    private async Task WaitForConnectionAsync(TimeSpan connectTimeout, CancellationToken ct)
    {
        using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(connectTimeout);

        try
        {
            await pipe.WaitForConnectionAsync(connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Hosted app did not connect to the pipe within {connectTimeout.TotalSeconds:F0}s.");
        }
    }

    private async Task<MessageEnvelope> ReadHelloAsync(CancellationToken ct)
    {
        MessageEnvelope? envelope = await reader.ReadAsync(ct).ConfigureAwait(false);
        if (envelope is null)
            throw new IOException("Hosted app closed the pipe before sending hello.");
        if (envelope.Type != MessageTypes.Hello)
            throw new InvalidOperationException(
                $"Expected '{MessageTypes.Hello}' from hosted app; got '{envelope.Type}'.");
        return envelope;
    }

    private static HelloPayload ParseHelloPayload(MessageEnvelope envelope)
    {
        HelloPayload? payload = envelope.Data?.Deserialize(HuskyJsonContext.Default.HelloPayload);
        if (payload is null)
            throw new InvalidOperationException("Hosted app's hello message is missing its payload.");
        return payload;
    }

    public async Task<bool> SendPingAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (receiverLoop is null)
            throw new InvalidOperationException("Cannot send ping before the handshake completes.");

        string id = Guid.NewGuid().ToString("D");
        TaskCompletionSource pongTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingPings[id] = pongTcs;

        try
        {
            MessageEnvelope envelope = new() { Id = id, Type = MessageTypes.Ping };
            await WriteAsync(envelope, ct).ConfigureAwait(false);

            try
            {
                await pongTcs.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException) { return false; }
        }
        finally
        {
            pendingPings.TryRemove(id, out _);
        }
    }

    public async Task SendShutdownAsync(
        string reason,
        TimeSpan totalTimeout,
        TimeSpan ackTimeout,
        CancellationToken ct = default)
    {
        if (receiverLoop is null)
            throw new InvalidOperationException("Cannot send shutdown before the handshake completes.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        string id = Guid.NewGuid().ToString("D");
        TaskCompletionSource ackTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        shutdownAckTcs = ackTcs;
        shutdownAckExpectedFor = id;

        ShutdownPayload payload = new(reason, (int)totalTimeout.TotalSeconds);
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.ShutdownPayload);
        MessageEnvelope envelope = new()
        {
            Id = id,
            Type = MessageTypes.Shutdown,
            Data = data,
        };

        await WriteAsync(envelope, ct).ConfigureAwait(false);
        await ackTcs.Task.WaitAsync(ackTimeout, ct).ConfigureAwait(false);
    }

    private async Task SendWelcomeAsync(
        string? replyTo, bool accepted, string? reason, CancellationToken ct)
    {
        WelcomePayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            LauncherVersion: LauncherVersion,
            Accepted: accepted,
            Reason: reason);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.WelcomePayload);
        MessageEnvelope envelope = new()
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = replyTo,
            Type = MessageTypes.Welcome,
            Data = data,
        };

        await WriteAsync(envelope, ct).ConfigureAwait(false);
    }

    private async Task WriteAsync(MessageEnvelope envelope, CancellationToken ct)
    {
        await writeMutex.WaitAsync(ct).ConfigureAwait(false);
        try { await writer.WriteAsync(envelope, ct).ConfigureAwait(false); }
        finally { writeMutex.Release(); }
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
                    return;
                }

                if (envelope is null) return;

                // §8.1: every received message resets the watchdog's activity
                // timestamp, regardless of type.
                OnActivity?.Invoke();

                switch (envelope.Type)
                {
                    case MessageTypes.ShutdownAck
                        when envelope.ReplyTo is { Length: > 0 } shutdownReplyTo
                             && shutdownReplyTo == shutdownAckExpectedFor:
                        shutdownAckTcs?.TrySetResult();
                        break;

                    case MessageTypes.Pong
                        when envelope.ReplyTo is { Length: > 0 } pongReplyTo
                             && pendingPings.TryGetValue(pongReplyTo, out TaskCompletionSource? pongTcs):
                        pongTcs.TrySetResult();
                        break;

                    // heartbeat / unknown types: drop per §3.6.
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        if (OperatingSystem.IsWindows())
            return CreateWindowsPipe(pipeName);

        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: PipeBufferSize,
            outBufferSize: PipeBufferSize);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe(string pipeName)
    {
        SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Could not determine current user SID.");

        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(
            owner,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: PipeBufferSize,
            outBufferSize: PipeBufferSize,
            pipeSecurity: security);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try { lifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }

        if (receiverLoop is not null)
        {
            try { await receiverLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { /* loop did not drain — proceed */ }
            catch (OperationCanceledException) { /* normal */ }
        }

        reader.Dispose();
        await writer.DisposeAsync().ConfigureAwait(false);
        await pipe.DisposeAsync().ConfigureAwait(false);

        writeMutex.Dispose();
        lifetimeCts.Dispose();
    }
}
