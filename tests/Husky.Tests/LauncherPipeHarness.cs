using System.IO.Pipes;
using Husky;
using Husky.Protocol;

namespace Husky.Tests;

internal sealed class LauncherPipeHarness : IAsyncDisposable
{
    public AppPipeServer Server { get; }
    public NamedPipeClientStream Client { get; }
    public MessageReader ClientReader { get; }
    public MessageWriter ClientWriter { get; }

    private LauncherPipeHarness(AppPipeServer server, NamedPipeClientStream client)
    {
        Server = server;
        Client = client;
        ClientReader = new MessageReader(client, leaveOpen: true);
        ClientWriter = new MessageWriter(client, leaveOpen: true);
    }

    public static async Task<LauncherPipeHarness> CreateConnectedAsync(
        string launcherVersion = "1.0.0-test")
    {
        string name = $"husky-test-{Guid.NewGuid():N}";
        AppPipeServer server = AppPipeServer.Create(name, launcherVersion);
        NamedPipeClientStream client = new(
            ".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout: 5_000);
        }
        catch
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
            throw;
        }
        return new LauncherPipeHarness(server, client);
    }

    public static AppPipeServer CreateUnconnectedServer(string launcherVersion = "1.0.0-test")
    {
        string name = $"husky-test-{Guid.NewGuid():N}";
        return AppPipeServer.Create(name, launcherVersion);
    }

    public async ValueTask DisposeAsync()
    {
        ClientReader.Dispose();
        await ClientWriter.DisposeAsync();
        await Client.DisposeAsync();
        await Server.DisposeAsync();
    }
}
