using Husky.Protocol;

namespace Husky;

/// <summary>
/// Builds a fresh <see cref="AppSession"/> for one launch of the hosted app
/// (LEASH §5.4). Generates a per-launch pipe name, starts the process,
/// performs the hello/welcome handshake, wires the watchdog activity sink,
/// then hands the wired session back to the caller.
/// </summary>
internal sealed class AppSessionLauncher(
    string executablePath,
    string appName,
    string launcherVersion,
    WatchdogOptions watchdogOptions,
    Action<string> onStandardOutput,
    Action<string> onStandardError)
{
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    public async Task<AppSession> StartAsync(
        Action<AppSession> onAppDeclaredDead,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(onAppDeclaredDead);

        string pipeName = PipeNaming.Generate();
        AppPipeServer pipeServer = AppPipeServer.Create(pipeName, launcherVersion);

        AppProcess? process = null;
        Watchdog? watchdog = null;
        try
        {
            AppProcessOptions options = new(
                ExecutablePath: executablePath,
                WorkingDirectory: Path.GetDirectoryName(executablePath)!,
                Environment: new Dictionary<string, string?>
                {
                    [HuskyEnvironment.PipeNameVariable] = pipeName,
                    [HuskyEnvironment.AppNameVariable] = appName,
                });

            // The watchdog isn't built until after the handshake — but the
            // process needs activity callbacks now, so we bind through a
            // late-bound delegate.
            Action recordActivity = () => { };
            process = AppProcess.Start(
                options,
                onStandardOutput: line => { recordActivity(); onStandardOutput(line); },
                onStandardError: line => { recordActivity(); onStandardError(line); });

            await pipeServer.AcceptAndHandshakeAsync(HandshakeTimeout, ct).ConfigureAwait(false);

            ConnectedApp connected = pipeServer.ConnectedApp
                ?? throw new InvalidOperationException("Handshake completed without a connected app.");

            watchdog = new Watchdog(pipeServer.SendPingAsync, watchdogOptions);
            recordActivity = watchdog.RecordActivity;
            pipeServer.OnActivity = watchdog.RecordActivity;

            AppSession session = new(pipeServer, process, watchdog, connected);
            watchdog.OnAppDeclaredDead = () => onAppDeclaredDead(session);
            watchdog.Start();

            return session;
        }
        catch
        {
            // Roll back partial construction in reverse order.
            if (watchdog is not null) await watchdog.DisposeAsync().ConfigureAwait(false);
            if (process is not null) await process.DisposeAsync().ConfigureAwait(false);
            await pipeServer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
