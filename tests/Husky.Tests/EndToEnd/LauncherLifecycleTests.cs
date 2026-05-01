using System.Collections.Concurrent;

namespace Husky.Tests.EndToEnd;

public sealed class LauncherLifecycleTests
{
    [Fact]
    public async Task Launcher_starts_TestApp_completes_handshake_and_forwards_output()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        staged.WriteDefaultConfig(name: "smoke-app");

        ConcurrentQueue<string> stdoutLines = new();
        System.Diagnostics.Process launcher = staged.Start(onStandardOutput: stdoutLines.Enqueue);

        // Banner line, "starting smoke-app", forwarded "testapp: ready", and finally
        // the "attached" log line all flow over stdout once the handshake completes.
        // (Spectre.Console may wrap long lines across multiple stdout lines on narrow
        // virtual terminals — match each fragment independently.)
        await WaitForFragmentAsync(stdoutLines, "attached", TimeSpan.FromSeconds(20));

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
    public async Task Launcher_exits_with_code_2_when_executable_path_does_not_exist()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        staged.WriteConfig("""
            {
              "name": "smoke-app",
              "executable": "app/does-not-exist.exe",
              "source": {
                "type": "github",
                "repo": "x/y",
                "asset": "y-{version}.zip"
              }
            }
            """);

        System.Diagnostics.Process launcher = staged.Start();

        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));

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
