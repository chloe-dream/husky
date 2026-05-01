using System.Text.Json;
using Husky.Client;
using Husky.Protocol;

namespace Husky.Client.Tests;

/// <summary>
/// Test helper: sets up a paired pipe + completes the hello/welcome handshake,
/// returning a ready-to-use HuskyClient and the paired server-side harness.
/// </summary>
internal sealed class ConnectedHandshake : IAsyncDisposable
{
    public PipeTestHarness Harness { get; }
    public HuskyClient Client { get; }

    private ConnectedHandshake(PipeTestHarness harness, HuskyClient client)
    {
        Harness = harness;
        Client = client;
    }

    public static async Task<ConnectedHandshake> PerformAsync(
        HuskyClientOptions? options = null,
        string appName = "test-app")
    {
        PipeTestHarness harness = await PipeTestHarness.CreateAsync();
        try
        {
            Task<HuskyClient> attachTask = HuskyClient.AttachOnStreamAsync(
                harness.Client,
                appName,
                options ?? HuskyClientOptions.Default,
                CancellationToken.None);

            MessageEnvelope? hello = await harness.ServerReader.ReadAsync();
            await SendWelcomeAsync(harness.ServerWriter, hello!.Id);

            HuskyClient client = await attachTask;
            return new ConnectedHandshake(harness, client);
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Harness.DisposeAsync();
    }

    private static async Task SendWelcomeAsync(MessageWriter writer, string? helloId)
    {
        WelcomePayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            LauncherVersion: "1.0.0-test",
            Accepted: true,
            Reason: null);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.WelcomePayload);
        MessageEnvelope envelope = new()
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = helloId,
            Type = MessageTypes.Welcome,
            Data = data,
        };

        await writer.WriteAsync(envelope);
    }
}
