using System.Text.Json;
using Husky;
using Husky.Protocol;

namespace Husky.Tests;

public sealed class AppPipeServerTests
{
    [Fact]
    public async Task Handshake_succeeds_when_app_sends_valid_hello_and_populates_ConnectedApp()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        await SendHelloAsync(h.ClientWriter, "test-app", appVersion: "1.2.3", pid: 999);

        MessageEnvelope welcome = await ReadAsync(h.ClientReader);
        Assert.Equal(MessageTypes.Welcome, welcome.Type);
        WelcomePayload? payload = welcome.Data?.Deserialize(HuskyJsonContext.Default.WelcomePayload);
        Assert.NotNull(payload);
        Assert.True(payload!.Accepted);
        Assert.Null(payload.Reason);
        Assert.Equal(ProtocolVersion.Current, payload.ProtocolVersion);
        Assert.Equal("1.0.0-test", payload.LauncherVersion);

        await accept;

        Assert.NotNull(h.Server.ConnectedApp);
        Assert.Equal("test-app", h.Server.ConnectedApp!.Name);
        Assert.Equal("1.2.3", h.Server.ConnectedApp.Version);
        Assert.Equal(999, h.Server.ConnectedApp.Pid);
    }

    [Fact]
    public async Task Welcome_replyTo_matches_hello_id()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        string helloId = await SendHelloAsync(h.ClientWriter, "test-app", "1.2.3", 1);
        MessageEnvelope welcome = await ReadAsync(h.ClientReader);

        Assert.Equal(helloId, welcome.ReplyTo);

        await accept;
    }

    [Fact]
    public async Task Handshake_rejects_app_with_mismatched_protocol_major_version()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        int wrongVersion = ProtocolVersion.Current + 1;
        await SendHelloAsync(
            h.ClientWriter, "test-app", "1.2.3", 1, protocolVersion: wrongVersion);

        // App should still receive a welcome, but with accepted=false.
        MessageEnvelope welcome = await ReadAsync(h.ClientReader);
        WelcomePayload? payload = welcome.Data?.Deserialize(HuskyJsonContext.Default.WelcomePayload);

        Assert.NotNull(payload);
        Assert.False(payload!.Accepted);
        Assert.NotNull(payload.Reason);
        Assert.Contains("protocol version", payload.Reason!, StringComparison.OrdinalIgnoreCase);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await accept);
        Assert.Contains("protocol version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handshake_throws_when_first_message_is_not_hello()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        await h.ClientWriter.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await accept);
        Assert.Contains(MessageTypes.Hello, ex.Message, StringComparison.Ordinal);
        Assert.Contains(MessageTypes.Heartbeat, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handshake_throws_when_hello_has_no_payload()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        await h.ClientWriter.WriteAsync(new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString("D"),
            Type = MessageTypes.Hello,
            // Data deliberately absent.
        });

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await accept);
        Assert.Contains("payload", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handshake_throws_IOException_when_app_closes_pipe_before_hello()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));

        await h.Client.DisposeAsync();

        await Assert.ThrowsAsync<IOException>(async () => await accept);
    }

    [Fact]
    public async Task Handshake_throws_TimeoutException_when_no_app_connects_within_timeout()
    {
        await using AppPipeServer server = LauncherPipeHarness.CreateUnconnectedServer();

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await server.AcceptAndHandshakeAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task Calling_AcceptAndHandshake_twice_throws()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));
        await SendHelloAsync(h.ClientWriter, "test-app", "1.2.3", 1);
        await ReadAsync(h.ClientReader); // welcome
        await accept;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Receiver_loop_silently_drops_heartbeats_and_unknown_messages()
    {
        await using LauncherPipeHarness h = await LauncherPipeHarness.CreateConnectedAsync();

        Task accept = h.Server.AcceptAndHandshakeAsync(TimeSpan.FromSeconds(5));
        await SendHelloAsync(h.ClientWriter, "test-app", "1.2.3", 1);
        await ReadAsync(h.ClientReader); // welcome
        await accept;

        // Send a few heartbeats and an unknown type. The loop must not crash.
        await h.ClientWriter.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });
        await h.ClientWriter.WriteAsync(new MessageEnvelope { Type = "future-message" });
        await h.ClientWriter.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });

        // Brief settle, then close the pipe and confirm the loop exited cleanly.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        await h.Client.DisposeAsync();

        Assert.NotNull(h.Server.ReceiverTask);
        await h.Server.ReceiverTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(h.Server.ReceiverTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PipeName_is_exposed_after_Create()
    {
        const string name = "husky-test-explicit-name";
        await using AppPipeServer server = AppPipeServer.Create(name, "1.0.0-test");

        Assert.Equal(name, server.PipeName);
    }

    [Fact]
    public void Create_throws_when_pipe_name_is_blank()
    {
        Assert.Throws<ArgumentException>(() => AppPipeServer.Create("   ", "1.0.0-test"));
    }

    [Fact]
    public void Create_throws_when_launcher_version_is_blank()
    {
        Assert.Throws<ArgumentException>(() =>
            AppPipeServer.Create($"husky-test-{Guid.NewGuid():N}", "  "));
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        AppPipeServer server = LauncherPipeHarness.CreateUnconnectedServer();

        await server.DisposeAsync();
        await server.DisposeAsync(); // must not throw
    }

    private static async Task<string> SendHelloAsync(
        MessageWriter writer,
        string appName,
        string appVersion,
        int pid,
        int? protocolVersion = null)
    {
        HelloPayload payload = new(
            ProtocolVersion: protocolVersion ?? ProtocolVersion.Current,
            AppVersion: appVersion,
            AppName: appName,
            Pid: pid);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.HelloPayload);
        string id = Guid.NewGuid().ToString("D");

        await writer.WriteAsync(new MessageEnvelope
        {
            Id = id,
            Type = MessageTypes.Hello,
            Data = data,
        });

        return id;
    }

    private static async Task<MessageEnvelope> ReadAsync(MessageReader reader)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        MessageEnvelope? envelope = await reader.ReadAsync(cts.Token);
        Assert.NotNull(envelope);
        return envelope!;
    }
}
