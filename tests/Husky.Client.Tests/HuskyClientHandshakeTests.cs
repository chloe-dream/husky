using System.Text.Json;
using Husky.Client;
using Husky.Protocol;

namespace Husky.Client.Tests;

[Collection(EnvVarCollection.Name)]
public sealed class HuskyClientHandshakeTests
{
    [Fact]
    public void IsHosted_is_true_when_pipe_env_var_has_a_value()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, "some-pipe-name");

        Assert.True(HuskyClient.IsHosted);
    }

    [Fact]
    public void IsHosted_is_false_when_pipe_env_var_is_unset_or_empty()
    {
        using EnvVarScope a = new(HuskyEnvironment.PipeNameVariable, null);
        Assert.False(HuskyClient.IsHosted);

        using EnvVarScope b = new(HuskyEnvironment.PipeNameVariable, string.Empty);
        Assert.False(HuskyClient.IsHosted);
    }

    [Fact]
    public async Task AttachIfHostedAsync_returns_null_when_not_hosted()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, null);

        HuskyClient? client = await HuskyClient.AttachIfHostedAsync();

        Assert.Null(client);
    }

    [Fact]
    public async Task AttachAsync_throws_when_not_hosted()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, null);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await HuskyClient.AttachAsync());
    }

    [Fact]
    public async Task Handshake_succeeds_when_launcher_accepts()
    {
        await using PipeTestHarness harness = await PipeTestHarness.CreateAsync();

        Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
            harness.Client, "test-app", HuskyClientOptions.Default, CancellationToken.None);

        MessageEnvelope? hello = await harness.ServerReader.ReadAsync();
        Assert.NotNull(hello);
        await SendWelcomeAsync(harness.ServerWriter, hello!.Id, accepted: true);

        await using HuskyClient client = await attachTask;

        Assert.Equal("test-app", client.AppName);
    }

    [Fact]
    public async Task Hello_payload_carries_protocol_version_and_app_metadata()
    {
        await using PipeTestHarness harness = await PipeTestHarness.CreateAsync();

        Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
            harness.Client, "umbrella-bot", HuskyClientOptions.Default, CancellationToken.None);

        MessageEnvelope? envelope = await harness.ServerReader.ReadAsync();
        Assert.NotNull(envelope);
        Assert.Equal(MessageTypes.Hello, envelope!.Type);
        Assert.NotNull(envelope.Id);
        Assert.NotNull(envelope.Data);

        HelloPayload? hello = envelope.Data!.Value.Deserialize(HuskyJsonContext.Default.HelloPayload);
        Assert.NotNull(hello);
        Assert.Equal(ProtocolVersion.Current, hello!.ProtocolVersion);
        Assert.Equal("umbrella-bot", hello.AppName);
        Assert.Equal(Environment.ProcessId, hello.Pid);
        Assert.False(string.IsNullOrEmpty(hello.AppVersion));

        await SendWelcomeAsync(harness.ServerWriter, envelope.Id, accepted: true);
        await using HuskyClient client = await attachTask;
    }

    [Fact]
    public async Task Handshake_throws_when_launcher_rejects()
    {
        await using PipeTestHarness harness = await PipeTestHarness.CreateAsync();

        Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
            harness.Client, "test-app", HuskyClientOptions.Default, CancellationToken.None);

        MessageEnvelope? hello = await harness.ServerReader.ReadAsync();
        await SendWelcomeAsync(
            harness.ServerWriter, hello!.Id, accepted: false, reason: "protocol version mismatch");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await attachTask);

        Assert.Contains("protocol version mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handshake_throws_TimeoutException_when_welcome_never_arrives()
    {
        await using PipeTestHarness harness = await PipeTestHarness.CreateAsync();
        HuskyClientOptions options = HuskyClientOptions.Default with
        {
            WelcomeTimeout = TimeSpan.FromMilliseconds(200),
        };

        Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
            harness.Client, "test-app", options, CancellationToken.None);

        // Server reads the hello but does not respond.
        await harness.ServerReader.ReadAsync();

        await Assert.ThrowsAsync<TimeoutException>(async () => await attachTask);
    }

    [Fact]
    public async Task Handshake_throws_when_pipe_closes_before_welcome()
    {
        await using PipeTestHarness harness = await PipeTestHarness.CreateAsync();

        Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
            harness.Client, "test-app", HuskyClientOptions.Default, CancellationToken.None);

        await harness.ServerReader.ReadAsync();
        // Close the server side without sending a welcome.
        harness.Server.Disconnect();

        await Assert.ThrowsAsync<IOException>(async () => await attachTask);
    }

    private static async Task SendWelcomeAsync(
        MessageWriter writer, string? helloId, bool accepted, string? reason = null)
    {
        WelcomePayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            LauncherVersion: "1.0.0-test",
            Accepted: accepted,
            Reason: reason);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.WelcomePayload);
        MessageEnvelope envelope = new()
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = helloId,
            Type = MessageTypes.Welcome,
            Data = data,
        };

        await writer.WriteAsync(envelope).ConfigureAwait(false);
    }
}

/// <summary>
/// Sets an environment variable for the lifetime of the scope and restores
/// the previous value on disposal. Tests using this must run in the
/// EnvVarCollection so they do not race each other.
/// </summary>
internal sealed class EnvVarScope : IDisposable
{
    private readonly string name;
    private readonly string? previous;

    public EnvVarScope(string name, string? value)
    {
        this.name = name;
        previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvVarCollection
{
    public const string Name = "EnvironmentVariables";
}
