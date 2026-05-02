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
    private TaskCompletionSource<bool>? updateInFlight;
    private TaskCompletionSource declaredDeadTrigger = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource sessionStartedTrigger = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<int> RunAsync(CancellationToken graceful, CancellationToken hardKill)
    {
        // Boot-time update check (LEASH §5.3.5).
        bool installed = File.Exists(executablePath);
        string currentVersion = AppVersionReader.ReadCurrent(executablePath);

        if (!installed)
        {
            ConsoleOutput.Husky("no app installed yet — bootstrapping.");
            string installedVersion;
            try
            {
                installedVersion = await BootstrapAsync(currentVersion, graceful).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Husky($"bootstrap failed: {ex.Message}");
                return ExitCodes.ConfigError;
            }
            ConsoleOutput.Husky($"update succeeded — now on v{installedVersion}");
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
                    ConsoleOutput.Husky($"update succeeded — now on v{bootCheck.Version}");
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
                ConsoleOutput.Husky("enough. lying down.");
                return ExitCodes.Generic;
            }

            Task gracefulWait = AwaitCancellationAsync(graceful);
            Task deadWait;
            lock (sessionGate) deadWait = declaredDeadTrigger.Task;
            Task winner = await Task.WhenAny(session.ExitTask, gracefulWait, deadWait).ConfigureAwait(false);

            if (winner == gracefulWait)
            {
                // If a polling-tick update is mid-flight, let it finish (or
                // tear it down via the hard-kill token on a double Ctrl+C)
                // before we drive our own shutdown — otherwise we'd race
                // StopCurrentSessionAsync with a duplicate stop.
                Task<bool>? pending = PeekUpdateInFlight();
                if (pending is not null)
                {
                    ConsoleOutput.Husky("update in flight — letting it finish before we sit.");
                    try { await pending.WaitAsync(hardKill).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (hardKill.IsCancellationRequested) { /* fall through */ }
                }

                ConsoleOutput.Husky("asking app to sit.");
                AppSession? alive = CurrentSession;
                if (alive is not null && !alive.HasExited)
                {
                    await GracefulShutdown.StopAsync(
                        alive,
                        reason: "launcher-stopping",
                        shutdownTimeout: TimeSpan.FromSeconds(config.ShutdownTimeoutSec),
                        killAfter: TimeSpan.FromSeconds(config.KillAfterSec),
                        hardKill: hardKill).ConfigureAwait(false);
                }
                return ExitCodes.Ok;
            }

            if (winner == deadWait)
            {
                try { await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException) { }
            }

            // Session ended. If an update is driving this exit, hand control
            // back to the polling tick — it owns the next session.
            Task<bool>? updateAwait = TakeUpdateInFlight();
            if (updateAwait is not null)
            {
                bool updateOk;
                try { updateOk = await updateAwait.ConfigureAwait(false); }
                catch { updateOk = false; }

                if (updateOk && CurrentSession is not null) continue;

                // Update aborted with no fresh session — fall through to the
                // crash path so the restart policy applies.
            }

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
                bool revived = await ParkUntilUpdateOrInterruptAsync(graceful).ConfigureAwait(false);
                if (!revived) return ExitCodes.Ok;

                ConsoleOutput.Husky("update brought a fresh build — back online.");
                continue;
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

    /// <summary>
    /// Wait for either a graceful interrupt or a successful update that brings
    /// up a fresh session (LEASH §8.4: a successful update resets the cap and
    /// triggers a start). Returns true when a session is now available.
    /// </summary>
    private async Task<bool> ParkUntilUpdateOrInterruptAsync(CancellationToken graceful)
    {
        Task gracefulWait = AwaitCancellationAsync(graceful);
        Task sessionWait;
        lock (sessionGate) sessionWait = sessionStartedTrigger.Task;

        Task winner = await Task.WhenAny(gracefulWait, sessionWait).ConfigureAwait(false);
        return winner == sessionWait && CurrentSession is not null;
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
        TaskCompletionSource<bool> mark = StartUpdate();
        try
        {
            await updateFlow.RunAsync(
                update,
                stopAppAsync: c => StopCurrentSessionAsync(c),
                startAppAndAwaitHelloAsync: c => RestartAfterUpdateAsync(c),
                ct: ct).ConfigureAwait(false);
            restartPolicy.Reset();
            ConsoleOutput.Husky($"update succeeded — now on v{update.Version}");
            mark.TrySetResult(true);
        }
        catch (UpdateException ex)
        {
            ConsoleOutput.Husky($"update aborted: {ex.Message}");
            mark.TrySetResult(false);
        }
        catch (OperationCanceledException) { mark.TrySetResult(false); }
        catch (Exception ex)
        {
            ConsoleOutput.Husky($"update aborted: {ex.Message}");
            mark.TrySetResult(false);
        }
    }

    private async Task<string> BootstrapAsync(string currentVersion, CancellationToken ct)
    {
        UpdateInfo? update = await source.CheckForUpdateAsync(currentVersion, ct).ConfigureAwait(false);
        if (update is null)
            throw new UpdateException("source has no version available for bootstrap.");

        ConsoleOutput.Husky($"new version found: v{update.Version}");
        await updateFlow.RunAsync(
            update,
            stopAppAsync: _ => Task.CompletedTask,
            startAppAndAwaitHelloAsync: _ => Task.CompletedTask,
            ct: ct).ConfigureAwait(false);
        return update.Version;
    }

    private async Task ApplyUpdateAtBootAsync(UpdateInfo update, CancellationToken ct)
    {
        await updateFlow.RunAsync(
            update,
            stopAppAsync: _ => Task.CompletedTask,
            startAppAndAwaitHelloAsync: _ => Task.CompletedTask,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<AppSession> StartSessionAsync(CancellationToken ct)
    {
        // Reset the dead trigger so the supervisor only reacts to *this*
        // session's watchdog escalation.
        lock (sessionGate)
            declaredDeadTrigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
        TaskCompletionSource trigger;
        lock (sessionGate) trigger = declaredDeadTrigger;
        trigger.TrySetResult();
    }

    private static void AnnounceUp(AppSession session) =>
        ConsoleOutput.Husky($"{session.ConnectedApp.Name} v{session.ConnectedApp.Version} is up.");

    private AppSession? CurrentSession
    {
        get { lock (sessionGate) return currentSession; }
    }

    private void SetCurrentSession(AppSession session)
    {
        TaskCompletionSource? previousTrigger;
        lock (sessionGate)
        {
            currentSession = session;
            previousTrigger = sessionStartedTrigger;
            sessionStartedTrigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        previousTrigger.TrySetResult();
    }

    private TaskCompletionSource<bool> StartUpdate()
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sessionGate) updateInFlight = tcs;
        return tcs;
    }

    private Task<bool>? TakeUpdateInFlight()
    {
        TaskCompletionSource<bool>? tcs;
        lock (sessionGate)
        {
            tcs = updateInFlight;
            updateInFlight = null;
        }
        return tcs?.Task;
    }

    private Task<bool>? PeekUpdateInFlight()
    {
        lock (sessionGate) return updateInFlight?.Task;
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
