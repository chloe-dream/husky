using System.IO.Pipes;
using Husky.Protocol;

namespace Husky.Client.Tests;

internal sealed class PipeTestHarness : IAsyncDisposable
{
    public string PipeName { get; }
    public NamedPipeServerStream Server { get; }
    public NamedPipeClientStream Client { get; }
    public MessageReader ServerReader { get; }
    public MessageWriter ServerWriter { get; }

    private PipeTestHarness(
        string name,
        NamedPipeServerStream server,
        NamedPipeClientStream client)
    {
        PipeName = name;
        Server = server;
        Client = client;
        ServerReader = new MessageReader(server, leaveOpen: true);
        ServerWriter = new MessageWriter(server, leaveOpen: true);
    }

    public static async Task<PipeTestHarness> CreateAsync()
    {
        string name = $"husky-test-{Guid.NewGuid():N}";

        NamedPipeServerStream server = new(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 65_536,
            outBufferSize: 65_536);

        NamedPipeClientStream client = new(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Task connectServer = server.WaitForConnectionAsync();
        await client.ConnectAsync(timeout: 5_000).ConfigureAwait(false);
        await connectServer.ConfigureAwait(false);

        return new PipeTestHarness(name, server, client);
    }

    public async ValueTask DisposeAsync()
    {
        ServerReader.Dispose();
        await ServerWriter.DisposeAsync().ConfigureAwait(false);
        await Server.DisposeAsync().ConfigureAwait(false);
        await Client.DisposeAsync().ConfigureAwait(false);
    }
}
