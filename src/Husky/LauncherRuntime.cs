namespace Husky;

/// <summary>
/// Top-level launcher loop. Owns the conversation between the source
/// provider, the update flow, the app sessions, and the crash-restart
/// policy. Extracted from Program.cs so the wiring is testable.
/// </summary>
internal sealed class LauncherRuntime(
    HuskyConfig config,
    IUpdateSource source,
    UpdateFlow updateFlow,
    AppSessionLauncher sessionLauncher,
    RestartPolicy restartPolicy,
    string executablePath)
{
    private readonly object sessionGate = new();
    private AppSession? currentSession;
    private readonly TaskCompletionSource declaredDeadTrigger = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<int> RunAsync(CancellationToken graceful, CancellationToken hardKill)
    {
        // Boot-time update check (LEASH §5.3.5).
        bool installed = File.Exists(executablePath);
        string currentVersion = AppVersionReader.ReadCurrent(executablePath);

        if (!installed)
        {
            ConsoleOutput.Husky("no app installed yet — bootstrapping.");
            try
            {
                await BootstrapAsync(currentVersion, graceful).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Husky($"bootstrap failed: {ex.Message}");
                return ExitCodes.ConfigError;
            }
            currentVersion = AppVersionReader.ReadCurrent(executablePath);
        }
        else
        {
            try
            {
                UpdateInfo? bootCheck = await source.CheckForUpdateAsync(currentVersion, graceful).ConfigureAwait(false);
                if (bootCheck is not null)
                {
                    ConsoleOutput.Husky($"new version found: v{bootCheck.Version}");
                    await ApplyUpdateAtBootAsync(bootCheck, graceful).ConfigureAwait(false);
                    currentVersion = AppVersionReader.ReadCurrent(executablePath);
                }
                else
                {
                    ConsoleOutput.Husky("up to date.");
                }
            }
            catch (OperationCanceledException) when (graceful.IsCancellationRequested)
            {
                return ExitCodes.Ok;
            }
            catch (Exception ex)
            {
                ConsoleOutput.Husky($"update check failed: {ex.Message}");
                // Continue — boot-time check failure does not block startup
                // when an app is already installed.
            }
        }

        // Now start the app for the first time.
        AppSession? session;
        try
        {
            ConsoleOutput.Husky($"starting {config.Name}");
            session = await StartSessionAsync(graceful).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ConsoleOutput.Husky($"failed to start {config.Name}: {ex.Message}");
            return ExitCodes.Generic;
        }

        AnnounceUp(session);

        // Polling loop (LEASH §5.3.7).
        await using UpdateScheduler scheduler = new(
            interval: TimeSpan.FromMinutes(config.CheckMinutes),
            tickAsync: ct => PollOnceAsync(ct));
        scheduler.Start();

        try
        {
            return await SuperviseAsync(graceful, hardKill).ConfigureAwait(false);
        }
        finally
        {
            await DisposeCurrentSessionAsync().ConfigureAwait(false);
        }
    }

    private async Task<int> SuperviseAsync(CancellationToken graceful, CancellationToken hardKill)
    {
        while (true)
        {
            AppSession? session = CurrentSession;
            if (session is null)
            {
                // No active session and no restart pending — we're done.
                ConsoleOutput.Husky("enough. lying down.");
                return ExitCodes.Generic;
            }

            Task gracefulWait = AwaitCancellationAsync(graceful);
            Task deadWait = declaredDeadTrigger.Task;
            Task winner = await Task.WhenAny(session.ExitTask, gracefulWait, deadWait).ConfigureAwait(false);

            if (winner == gracefulWait)
            {
                ConsoleOutput.Husky("asking app to sit.");
                await GracefulShutdown.StopAsync(
                    session,
                    reason: "launcher-stopping",
                    shutdownTimeout: TimeSpan.FromSeconds(config.ShutdownTimeoutSec),
                    killAfter: TimeSpan.FromSeconds(config.KillAfterSec),
                    hardKill: hardKill).ConfigureAwait(false);
                return ExitCodes.Ok;
            }

            if (winner == deadWait)
            {
                // Watchdog already invoked Kill(); wait briefly for the exit
                // to settle so the restart path sees HasExited == true.
                try { await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }

            // Session ended (clean exit, crash, or watchdog kill).
            int exitCode = session.ExitCode;
            await DisposeCurrentSessionAsync().ConfigureAwait(false);

            if (exitCode == 0)
            {
                ConsoleOutput.Husky($"{config.Name} exited cleanly.");
                return ExitCodes.Ok;
            }

            ConsoleOutput.Husky($"{config.Name} exited with code {exitCode} — considering restart.");

            if (!restartPolicy.CanRestart())
            {
                ConsoleOutput.Husky("enough. lying down.");
                // Loop instead of exiting so a later update can still bring us back.
                await ParkUntilUpdateOrInterruptAsync(graceful).ConfigureAwait(false);
                return ExitCodes.Ok;
            }

            ConsoleOutput.Husky($"pausing {config.RestartPauseSec}s before restart.");
            try { await Task.Delay(TimeSpan.FromSeconds(config.RestartPauseSec), graceful).ConfigureAwait(false); }
            catch (OperationCanceledException) { return ExitCodes.Ok; }

            restartPolicy.RecordAttempt();
            try
            {
                AppSession revived = await StartSessionAsync(graceful).ConfigureAwait(false);
                AnnounceUp(revived);
            }
            catch (OperationCanceledException) when (graceful.IsCancellationRequested)
            {
                return ExitCodes.Ok;
            }
            catch (Exception ex)
            {
                ConsoleOutput.Husky($"restart failed: {ex.Message}");
            }
        }
    }

    private static async Task ParkUntilUpdateOrInterruptAsync(CancellationToken graceful)
    {
        // Broken state: the launcher idles, polling continues in the
        // scheduler, and a successful update flips us back into a running
        // session. For now we just wait for the graceful trigger.
        try { await AwaitCancellationAsync(graceful).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        AppSession? session = CurrentSession;
        string version = session is not null
            ? session.ConnectedApp.Version
            : AppVersionReader.ReadCurrent(executablePath);

        ConsoleOutput.Husky("sniffing for updates...");

        UpdateInfo? update;
        try
        {
            update = await source.CheckForUpdateAsync(version, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            ConsoleOutput.Husky($"update check failed: {ex.Message}");
            return;
        }

        if (update is null) return;

        ConsoleOutput.Husky($"new version found: v{update.Version}");
        try
        {
            await updateFlow.RunAsync(
                update,
                stopAppAsync: c => StopCurrentSessionAsync(c),
                startAppAndAwaitHelloAsync: c => RestartAfterUpdateAsync(c),
                ct: ct).ConfigureAwait(false);
            restartPolicy.Reset();
        }
        catch (UpdateException ex)
        {
            ConsoleOutput.Husky($"update aborted: {ex.Message}");
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        catch (Exception ex)
        {
            ConsoleOutput.Husky($"update aborted: {ex.Message}");
        }
    }

    private async Task BootstrapAsync(string currentVersion, CancellationToken ct)
    {
        UpdateInfo? update = await source.CheckForUpdateAsync(currentVersion, ct).ConfigureAwait(false);
        if (update is null)
            throw new UpdateException("source has no version available for bootstrap.");

        ConsoleOutput.Husky($"new version found: v{update.Version}");
        await updateFlow.RunAsync(
            update,
            stopAppAsync: _ => Task.CompletedTask,
            startAppAndAwaitHelloAsync: _ => Task.CompletedTask, // app start happens after bootstrap returns
            ct: ct).ConfigureAwait(false);
    }

    private async Task ApplyUpdateAtBootAsync(UpdateInfo update, CancellationToken ct)
    {
        // Update found before app start — same shape as bootstrap, app start
        // happens afterwards at the top of the runtime loop.
        await updateFlow.RunAsync(
            update,
            stopAppAsync: _ => Task.CompletedTask,
            startAppAndAwaitHelloAsync: _ => Task.CompletedTask,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<AppSession> StartSessionAsync(CancellationToken ct)
    {
        AppSession session = await sessionLauncher.StartAsync(OnSessionDeclaredDead, ct).ConfigureAwait(false);
        SetCurrentSession(session);
        return session;
    }

    private async Task StopCurrentSessionAsync(CancellationToken ct)
    {
        AppSession? session = CurrentSession;
        if (session is null) return;

        ConsoleOutput.Husky("asking app to sit.");
        await GracefulShutdown.StopAsync(
            session,
            reason: "update",
            shutdownTimeout: TimeSpan.FromSeconds(config.ShutdownTimeoutSec),
            killAfter: TimeSpan.FromSeconds(config.KillAfterSec),
            hardKill: ct).ConfigureAwait(false);

        await DisposeCurrentSessionAsync().ConfigureAwait(false);
    }

    private async Task RestartAfterUpdateAsync(CancellationToken ct)
    {
        AppSession session = await StartSessionAsync(ct).ConfigureAwait(false);
        AnnounceUp(session);
    }

    private void OnSessionDeclaredDead(AppSession session)
    {
        ConsoleOutput.Husky("no answer. growling.");
        session.Kill();
        declaredDeadTrigger.TrySetResult();
    }

    private static void AnnounceUp(AppSession session) =>
        ConsoleOutput.Husky($"{session.ConnectedApp.Name} v{session.ConnectedApp.Version} is up.");

    private AppSession? CurrentSession
    {
        get { lock (sessionGate) return currentSession; }
    }

    private void SetCurrentSession(AppSession session)
    {
        lock (sessionGate) currentSession = session;
    }

    private async Task DisposeCurrentSessionAsync()
    {
        AppSession? toDispose;
        lock (sessionGate)
        {
            toDispose = currentSession;
            currentSession = null;
        }
        if (toDispose is not null)
            await toDispose.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task AwaitCancellationAsync(CancellationToken token)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration _ = token.Register(() => tcs.TrySetResult());
        await tcs.Task.ConfigureAwait(false);
    }
}
