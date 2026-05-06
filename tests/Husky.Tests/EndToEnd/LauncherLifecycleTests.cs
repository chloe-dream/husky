using System.Collections.Concurrent;

namespace Husky.Tests.EndToEnd;

[Collection(EndToEndCollection.Name)]
public sealed class LauncherLifecycleTests
{
    [Fact]
    public async Task Launcher_starts_TestApp_completes_handshake_and_forwards_output()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        staged.WriteDefaultConfig(name: "smoke-app");

        ConcurrentQueue<string> stdoutLines = new();
        System.Diagnostics.Process launcher = staged.Start(onStandardOutput: stdoutLines.Enqueue);

        // After the handshake the launcher logs "<name> v<version> is up.".
        // ANSI status-word colouring breaks "is up" with escape sequences, so
        // we match a stable fragment that the highlighter does not touch.
        await WaitForFragmentAsync(stdoutLines, "smoke-app v1.0.0", TimeSpan.FromSeconds(30));

        Assert.Contains(stdoutLines, line =>
            line.Contains("starting smoke-app", StringComparison.Ordinal));
        Assert.Contains(stdoutLines, line =>
            line.Contains("testapp: ready", StringComparison.Ordinal));

        Assert.False(launcher.HasExited);
    }

    [Fact]
    public async Task Launcher_exits_with_code_2_when_config_file_is_missing()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        // Deliberately do NOT write a config file.

        System.Diagnostics.Process launcher = staged.Start();

        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(2, launcher.ExitCode);
    }

    [Fact]
    public async Task Launcher_exits_with_code_2_when_bootstrap_source_is_unreachable()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        // No app present + no reachable manifest → bootstrap fails → exit 2.
        File.Delete(staged.AppExecutablePath);
        staged.WriteConfig($$"""
            {
              "name": "smoke-app",
              "executable": "{{staged.AppRelativeExecutable}}",
              "source": {
                "type": "http",
                "manifest": "http://127.0.0.1:1/missing.json"
              }
            }
            """);

        System.Diagnostics.Process launcher = staged.Start();

        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, launcher.ExitCode);
    }

    [Fact]
    public async Task Launcher_exits_with_code_2_when_config_json_is_malformed()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        staged.WriteConfig("{ this is not valid json");

        System.Diagnostics.Process launcher = staged.Start();

        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(2, launcher.ExitCode);
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
