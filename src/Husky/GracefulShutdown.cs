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

        try
        {
            await session.SendShutdownAsync(reason, shutdownTimeout, AckTimeout, hardKill).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ConsoleOutput.Husky("no shutdown-ack — continuing anyway.");
        }
        catch (IOException)
        {
            ConsoleOutput.Husky("pipe is gone — proceeding to wait.");
        }
        catch (OperationCanceledException) when (hardKill.IsCancellationRequested)
        {
            await HardKillAsync(session, "double interrupt — taking it down.").ConfigureAwait(false);
            return;
        }

        if (await TryWaitForExitAsync(session, shutdownTimeout, hardKill).ConfigureAwait(false))
        {
            ConsoleOutput.Husky("app sat down.");
            return;
        }
        if (hardKill.IsCancellationRequested)
        {
            await HardKillAsync(session, "double interrupt — taking it down.").ConfigureAwait(false);
            return;
        }

        if (killAfter > TimeSpan.Zero
            && await TryWaitForExitAsync(session, killAfter, hardKill).ConfigureAwait(false))
        {
            ConsoleOutput.Husky("app sat down.");
            return;
        }

        await HardKillAsync(session, "app didn't respond. growling.").ConfigureAwait(false);
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
        session.Kill();
        try { await session.ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { /* tearing down */ }
        catch (OperationCanceledException) { /* normal */ }
    }
}
