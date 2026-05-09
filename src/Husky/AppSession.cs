using Husky.Protocol;

namespace Husky;

/// <summary>
/// One running instance of the hosted app. Bundles the pipe server, the
/// child process, and the watchdog so a single object owns and disposes
/// every per-launch resource. Created via <see cref="AppSessionLauncher"/>.
/// </summary>
internal sealed class AppSession : IAsyncDisposable
{
    private readonly AppPipeServer pipeServer;
    private readonly AppProcess process;
    private readonly Watchdog watchdog;
    private bool disposed;

    public ConnectedApp ConnectedApp { get; }
    public AppPipeServer PipeServer => pipeServer;
    public AppProcess Process => process;
    public Watchdog Watchdog => watchdog;
    public Task ExitTask => process.ExitTask;
    public int ExitCode => process.ExitCode;
    public bool HasExited => process.HasExited;

    internal AppSession(
        AppPipeServer pipeServer,
        AppProcess process,
        Watchdog watchdog,
        ConnectedApp connectedApp)
    {
        this.pipeServer = pipeServer;
        this.process = process;
        this.watchdog = watchdog;
        ConnectedApp = connectedApp;
    }

    public Task SendShutdownAsync(string reason, TimeSpan totalTimeout, TimeSpan ackTimeout, CancellationToken ct = default) =>
        pipeServer.SendShutdownAsync(reason, totalTimeout, ackTimeout, ct);

    public void Kill() => process.Kill();

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        // Watchdog first — silences any in-flight probe before the pipe goes
        // away (LEASH §7.3 cutover discipline).
        await watchdog.DisposeAsync().ConfigureAwait(false);
        await pipeServer.DisposeAsync().ConfigureAwait(false);
        await process.DisposeAsync().ConfigureAwait(false);
    }
}
