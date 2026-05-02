using System.Collections.Concurrent;

namespace Husky.Tests.EndToEnd;

public sealed class CrashRestartEndToEndTests
{
    [Fact]
    public async Task Launcher_restarts_a_crashing_app_until_the_cap_is_hit()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        staged.WriteConfig($$"""
            {
              "name": "crash-app",
              "executable": "{{staged.AppRelativeExecutable}}",
              "source": {
                "type": "http",
                "manifest": "http://127.0.0.1:1/missing.json"
              },
              "checkMinutes": 60,
              "shutdownTimeoutSec": 5,
              "killAfterSec": 1,
              "restartAttempts": 2,
              "restartPauseSec": 1
            }
            """);

        ConcurrentQueue<string> stdoutLines = new();
        System.Diagnostics.Process launcher = staged.Start(
            onStandardOutput: stdoutLines.Enqueue,
            extraEnv: new Dictionary<string, string?> { ["HUSKY_TESTAPP_MODE"] = "crash" });

        // The launcher should: see exit code 7, log "considering restart", pause,
        // try once more, hit the cap, and then "enough. lying down."
        // ("down" is highlighted so we match the un-coloured prefix.)
        await WaitForFragmentAsync(stdoutLines, "enough. lying", TimeSpan.FromSeconds(60));

        Assert.Contains(stdoutLines, l => l.Contains("exited with code 7", StringComparison.Ordinal));
        Assert.Contains(stdoutLines, l => l.Contains("considering restart", StringComparison.Ordinal));
        Assert.Contains(stdoutLines, l => l.Contains("pausing 1s", StringComparison.Ordinal));
    }

    private static async Task WaitForFragmentAsync(
        ConcurrentQueue<string> lines, string fragment, TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (lines.Any(line => line.Contains(fragment, StringComparison.Ordinal))) return;
            try { await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        throw new TimeoutException(
            $"Did not see a line containing '{fragment}' within {timeout}.\nLines so far:\n{string.Join("\n", lines)}");
    }
}
