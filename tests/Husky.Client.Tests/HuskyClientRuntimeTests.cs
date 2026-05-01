using System.Text.Json;
using Husky.Client;
using Husky.Protocol;

namespace Husky.Client.Tests;

public sealed class HuskyClientRuntimeTests
{
    [Fact]
    public async Task Heartbeats_arrive_periodically_at_the_server()
    {
        HuskyClientOptions options = HuskyClientOptions.Default with
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(80),
        };
        await using ConnectedHandshake handshake = await ConnectedHandshake.PerformAsync(options);

        int heartbeats = 0;
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        while (heartbeats < 3)
        {
            MessageEnvelope? envelope = await handshake.Harness.ServerReader.ReadAsync(cts.Token);
            if (envelope?.Type == MessageTypes.Heartbeat) heartbeats++;
        }

        Assert.Equal(3, heartbeats);
    }

    [Fact]
    public async Task Server_ping_yields_pong_with_default_healthy_status()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        string pingId = Guid.NewGuid().ToString("D");
        await h.Harness.ServerWriter.WriteAsync(
            new MessageEnvelope { Id = pingId, Type = MessageTypes.Ping });

        MessageEnvelope pong = await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.Pong);

        Assert.Equal(pingId, pong.ReplyTo);
        PongPayload? payload = pong.Data!.Value.Deserialize(HuskyJsonContext.Default.PongPayload);
        Assert.NotNull(payload);
        Assert.Equal("healthy", payload!.Status);
        Assert.Null(payload.Details);
    }

    [Fact]
    public async Task SetHealth_is_reflected_in_subsequent_pong()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();
        h.Client.SetHealth(() =>
            new HealthStatus(HealthState.Degraded).With("queue", 7));

        string pingId = Guid.NewGuid().ToString("D");
        await h.Harness.ServerWriter.WriteAsync(
            new MessageEnvelope { Id = pingId, Type = MessageTypes.Ping });

        MessageEnvelope pong = await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.Pong);
        PongPayload? payload = pong.Data!.Value.Deserialize(HuskyJsonContext.Default.PongPayload);

        Assert.Equal("degraded", payload!.Status);
        Assert.NotNull(payload.Details);
        Assert.Equal(7, payload.Details!["queue"].GetInt32());
    }

    [Fact]
    public async Task Shutdown_message_yields_a_shutdown_ack_with_replyTo()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        string shutdownId = Guid.NewGuid().ToString("D");
        await SendShutdownAsync(h.Harness.ServerWriter, shutdownId, "update", timeoutSeconds: 30);

        MessageEnvelope ack = await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.ShutdownAck);

        Assert.Equal(shutdownId, ack.ReplyTo);
    }

    [Fact]
    public async Task Shutdown_invokes_OnShutdown_handler_with_correct_reason()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        TaskCompletionSource<ShutdownReason> reasonTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Client.OnShutdown((reason, ct) =>
        {
            reasonTcs.TrySetResult(reason);
            return Task.CompletedTask;
        });

        await SendShutdownAsync(
            h.Harness.ServerWriter, Guid.NewGuid().ToString("D"), "update", timeoutSeconds: 30);

        ShutdownReason received = await reasonTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ShutdownReason.Update, received);
    }

    [Fact]
    public async Task ShutdownToken_fires_after_handler_completes()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        h.Client.OnShutdown((_, _) => Task.CompletedTask);

        await SendShutdownAsync(
            h.Harness.ServerWriter, Guid.NewGuid().ToString("D"), "manual", timeoutSeconds: 30);

        await WaitForCancellationAsync(TimeSpan.FromSeconds(3), h.Client.ShutdownToken);
    }

    [Fact]
    public async Task Heartbeats_pause_after_shutdown()
    {
        HuskyClientOptions options = HuskyClientOptions.Default with
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(80),
        };
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync(options);

        h.Client.OnShutdown((_, _) => Task.CompletedTask);

        // Wait for at least one heartbeat to confirm the loop is running.
        await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.Heartbeat);

        await SendShutdownAsync(
            h.Harness.ServerWriter, Guid.NewGuid().ToString("D"), "update", timeoutSeconds: 30);

        // Read the ack, then drain anything queued, then watch ~300ms for further heartbeats.
        await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.ShutdownAck);

        await DrainAsync(h.Harness.ServerReader, TimeSpan.FromMilliseconds(50));

        bool sawLateHeartbeat = false;
        using CancellationTokenSource watchCts = new(TimeSpan.FromMilliseconds(300));
        try
        {
            while (true)
            {
                MessageEnvelope? envelope = await h.Harness.ServerReader.ReadAsync(watchCts.Token);
                if (envelope is null) break;
                if (envelope.Type == MessageTypes.Heartbeat) { sawLateHeartbeat = true; break; }
            }
        }
        catch (OperationCanceledException) { /* watchdog timer fired — no late heartbeat */ }

        Assert.False(sawLateHeartbeat);
    }

    [Fact]
    public async Task Handler_exception_does_not_break_ShutdownToken_signal()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        h.Client.OnShutdown((_, _) => throw new InvalidOperationException("handler boom"));

        await SendShutdownAsync(
            h.Harness.ServerWriter, Guid.NewGuid().ToString("D"), "update", timeoutSeconds: 30);

        await WaitForCancellationAsync(TimeSpan.FromSeconds(3), h.Client.ShutdownToken);
    }

    [Fact]
    public async Task SetHealth_provider_exception_falls_back_to_Healthy()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();
        h.Client.SetHealth(() => throw new InvalidOperationException("provider boom"));

        string pingId = Guid.NewGuid().ToString("D");
        await h.Harness.ServerWriter.WriteAsync(
            new MessageEnvelope { Id = pingId, Type = MessageTypes.Ping });

        MessageEnvelope pong = await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.Pong);
        PongPayload? payload = pong.Data!.Value.Deserialize(HuskyJsonContext.Default.PongPayload);

        Assert.Equal("healthy", payload!.Status);
    }

    [Fact]
    public async Task Unknown_message_types_are_dropped_silently()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        // Send a future / unknown type. Client should not crash, not respond.
        await h.Harness.ServerWriter.WriteAsync(
            new MessageEnvelope { Type = "future-message" });

        // Then send a ping and assert it still works — proves the receiver loop survived.
        string pingId = Guid.NewGuid().ToString("D");
        await h.Harness.ServerWriter.WriteAsync(
            new MessageEnvelope { Id = pingId, Type = MessageTypes.Ping });

        MessageEnvelope pong = await ReadUntilAsync(h.Harness.ServerReader, MessageTypes.Pong);
        Assert.Equal(pingId, pong.ReplyTo);
    }

    private static async Task<MessageEnvelope> ReadUntilAsync(MessageReader reader, string type)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        while (true)
        {
            MessageEnvelope? envelope = await reader.ReadAsync(cts.Token);
            if (envelope is null)
                throw new IOException($"Pipe closed before a '{type}' arrived.");
            if (envelope.Type == type) return envelope;
        }
    }

    private static async Task DrainAsync(MessageReader reader, TimeSpan window)
    {
        using CancellationTokenSource cts = new(window);
        try
        {
            while (true)
            {
                MessageEnvelope? envelope = await reader.ReadAsync(cts.Token);
                if (envelope is null) return;
            }
        }
        catch (OperationCanceledException) { /* drained */ }
    }

    private static async Task SendShutdownAsync(
        MessageWriter writer, string id, string reason, int timeoutSeconds)
    {
        ShutdownPayload payload = new(reason, timeoutSeconds);
        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.ShutdownPayload);
        await writer.WriteAsync(new MessageEnvelope
        {
            Id = id,
            Type = MessageTypes.Shutdown,
            Data = data,
        });
    }

    private static async Task WaitForCancellationAsync(TimeSpan timeout, CancellationToken token)
    {
        TaskCompletionSource tcs = new();
        using CancellationTokenRegistration reg = token.Register(() => tcs.TrySetResult());
        Task winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout, CancellationToken.None));
        if (winner != tcs.Task)
            throw new TimeoutException($"CancellationToken did not fire within {timeout}.");
    }
}
