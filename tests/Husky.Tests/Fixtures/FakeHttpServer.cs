using System.Net;
using System.Net.Sockets;

namespace Husky.Tests.Fixtures;

/// <summary>
/// Minimal HttpListener-based file server for update-flow and source-provider
/// tests. GETs serve files from <paramref name="rootDirectory"/>; misses 404.
/// Binds to a free loopback port so multiple servers can run in parallel.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly string rootDirectory;
    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;
    private bool disposed;

    public Uri Address { get; }

    private FakeHttpServer(HttpListener listener, string rootDirectory, Uri address)
    {
        this.listener = listener;
        this.rootDirectory = rootDirectory;
        Address = address;
        loop = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
    }

    public static FakeHttpServer Start(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Directory.Exists(rootDirectory))
            throw new DirectoryNotFoundException(rootDirectory);

        int port = FindFreeLoopbackPort();
        string prefix = $"http://localhost:{port}/";
        HttpListener listener = new();
        listener.Prefixes.Add(prefix);
        listener.Start();

        return new FakeHttpServer(listener, rootDirectory, new Uri(prefix));
    }

    public Uri Url(string relativePath) =>
        new(Address, relativePath.TrimStart('/'));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            string requestPath = context.Request.Url!.AbsolutePath.TrimStart('/');
            string normalized = Uri.UnescapeDataString(requestPath).Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(rootDirectory, normalized);

            if (!File.Exists(fullPath))
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = 200;
            byte[] bytes = await File.ReadAllBytesAsync(fullPath).ConfigureAwait(false);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch
        {
            try { context.Response.Abort(); }
            catch { /* swallow */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }

        try { listener.Stop(); listener.Close(); }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) { }

        try { await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { }

        cts.Dispose();
    }

    private static int FindFreeLoopbackPort()
    {
        TcpListener tcp = new(IPAddress.Loopback, 0);
        tcp.Start();
        try { return ((IPEndPoint)tcp.LocalEndpoint).Port; }
        finally { tcp.Stop(); }
    }
}
