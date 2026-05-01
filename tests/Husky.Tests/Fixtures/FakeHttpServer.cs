using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Husky.Tests.Fixtures;

/// <summary>
/// Minimal HttpListener-based file server for update-flow and source-provider
/// tests. GETs serve files from <paramref name="rootDirectory"/>; routes added
/// via <see cref="Map"/> take precedence and let tests serve dynamic JSON or
/// custom status codes. Binds to a free loopback port so multiple servers can
/// run in parallel.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly string? rootDirectory;
    private readonly ConcurrentDictionary<string, RouteHandler> routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource cts = new();
    private readonly Task loop;
    private bool disposed;
    private int requestCount;

    public Uri Address { get; }

    public int RequestCount => Volatile.Read(ref requestCount);

    private FakeHttpServer(HttpListener listener, string? rootDirectory, Uri address)
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

        return Bind(rootDirectory);
    }

    /// <summary>Starts a server with no file root — only mapped routes respond.</summary>
    public static FakeHttpServer StartEmpty() => Bind(rootDirectory: null);

    private static FakeHttpServer Bind(string? rootDirectory)
    {
        int port = FindFreeLoopbackPort();
        string prefix = $"http://localhost:{port}/";
        HttpListener listener = new();
        listener.Prefixes.Add(prefix);
        listener.Start();

        return new FakeHttpServer(listener, rootDirectory, new Uri(prefix));
    }

    /// <summary>Registers a handler for an absolute path (eg "/repos/x/y/releases/latest").</summary>
    public void Map(string absolutePath, RouteHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(handler);
        routes[NormalizePath(absolutePath)] = handler;
    }

    public void MapJson(string absolutePath, string json, int statusCode = 200)
    {
        Map(absolutePath, _ => new RouteResponse(
            StatusCode: statusCode,
            ContentType: "application/json",
            Body: Encoding.UTF8.GetBytes(json)));
    }

    public void MapStatus(string absolutePath, int statusCode)
    {
        Map(absolutePath, _ => new RouteResponse(StatusCode: statusCode, ContentType: null, Body: []));
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
        Interlocked.Increment(ref requestCount);
        try
        {
            string absolutePath = NormalizePath(context.Request.Url!.AbsolutePath);
            if (routes.TryGetValue(absolutePath, out RouteHandler? handler))
            {
                RouteResponse response = handler(context.Request);
                context.Response.StatusCode = response.StatusCode;
                if (response.ContentType is not null)
                    context.Response.ContentType = response.ContentType;
                context.Response.ContentLength64 = response.Body.Length;
                if (response.Body.Length > 0)
                    await context.Response.OutputStream.WriteAsync(response.Body).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (rootDirectory is null)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

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

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.StartsWith('/') ? path : "/" + path;
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

internal delegate RouteResponse RouteHandler(HttpListenerRequest request);

internal readonly record struct RouteResponse(int StatusCode, string? ContentType, byte[] Body);
