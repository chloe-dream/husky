using Retro.Crt;

namespace Husky;

/// <summary>
/// Drives the LEASH §5.5 graceful-shutdown sequence: send shutdown,
/// wait for ack, wait for exit within the configured timeout, fall back
/// to <see cref="AppProcess.Kill"/> if the app does not honour the request.
/// </summary>
internal static class GracefulShutdown
{
    public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(5);

    public static async Task StopAsync(
        AppSession session,
        string reason,
        TimeSpan shutdownTimeout,
        TimeSpan killAfter,
        CancellationToken hardKill = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (session.HasExited) return;

        if (hardKill.IsCancellationRequested)
        {
            await HardKillAsync(session, "double interrupt — taking it down.").ConfigureAwait(false);
            return;
        }

        bool needsHardKill = false;
        using (ConsoleOutput.BeginLiveWidget())
        {
            using var spinner = Spinner.Show(
                "asking app to sit", SpinnerStyle.Pipe, Color.LightCyan);

            try
            {
                await session.SendShutdownAsync(
                    reason, shutdownTimeout, AckTimeout, hardKill).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                spinner.Update("no shutdown-ack — waiting anyway");
            }
            catch (IOException)
            {
                spinner.Update("pipe is gone — waiting for exit");
            }
            catch (OperationCanceledException) when (hardKill.IsCancellationRequested)
            {
                spinner.Stop("double interrupt — taking it down.", Color.LightRed);
                await KillAndDrainAsync(session).ConfigureAwait(false);
                return;
            }

            if (await TryWaitForExitAsync(session, shutdownTimeout, hardKill).ConfigureAwait(false))
            {
                spinner.Stop("app sat down.", Color.LightGreen);
                return;
            }
            if (hardKill.IsCancellationRequested)
            {
                spinner.Stop("double interrupt — taking it down.", Color.LightRed);
                await KillAndDrainAsync(session).ConfigureAwait(false);
                return;
            }

            if (killAfter > TimeSpan.Zero)
            {
                spinner.Update(
                    $"grace period (+{killAfter.TotalSeconds:0}s)");
                if (await TryWaitForExitAsync(session, killAfter, hardKill).ConfigureAwait(false))
                {
                    spinner.Stop("app sat down.", Color.LightGreen);
                    return;
                }
                if (hardKill.IsCancellationRequested)
                {
                    spinner.Stop("double interrupt — taking it down.", Color.LightRed);
                    await KillAndDrainAsync(session).ConfigureAwait(false);
                    return;
                }
            }

            spinner.Stop("app didn't respond. growling.", Color.Yellow);
            needsHardKill = true;
        }

        if (needsHardKill)
            await KillAndDrainAsync(session).ConfigureAwait(false);
    }

    private static async Task<bool> TryWaitForExitAsync(
        AppSession session, TimeSpan timeout, CancellationToken hardKill)
    {
        try
        {
            await session.ExitTask.WaitAsync(timeout, hardKill).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    private static async Task HardKillAsync(AppSession session, string message)
    {
        ConsoleOutput.Husky(message);
        await KillAndDrainAsync(session).ConfigureAwait(false);
    }

    private static async Task KillAndDrainAsync(AppSession session)
    {
        session.Kill();
        try { await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { /* tearing down */ }
        catch (OperationCanceledException) { /* normal */ }
    }
}
