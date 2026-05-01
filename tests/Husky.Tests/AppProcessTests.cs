using System.Collections.Concurrent;
using Husky;

namespace Husky.Tests;

public sealed class AppProcessTests
{
    [Fact]
    public async Task Start_runs_executable_and_completes_with_exit_code_zero_in_standalone_mode()
    {
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            // No HUSKY_PIPE → TestApp falls into its standalone branch and exits 0.
            Environment: new Dictionary<string, string?>());

        await using AppProcess app = AppProcess.Start(options);

        await app.ExitTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(app.HasExited);
        Assert.Equal(0, app.ExitCode);
    }

    [Fact]
    public async Task Standard_output_lines_are_forwarded_to_handler()
    {
        ConcurrentQueue<string> stdoutLines = new();
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>());

        await using AppProcess app = AppProcess.Start(
            options,
            onStandardOutput: stdoutLines.Enqueue);

        await app.ExitTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(stdoutLines, line => line.Contains("testapp: ready", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Standard_error_lines_are_forwarded_to_handler()
    {
        ConcurrentQueue<string> stderrLines = new();
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>());

        await using AppProcess app = AppProcess.Start(
            options,
            onStandardError: stderrLines.Enqueue);

        await app.ExitTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(stderrLines, line => line.Contains("testapp: hello stderr", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Environment_variables_are_propagated_to_the_child_process()
    {
        ConcurrentQueue<string> stdoutLines = new();
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>
            {
                ["HUSKY_TESTAPP_MODE"] = "wait",
            });

        await using AppProcess app = AppProcess.Start(
            options,
            onStandardOutput: stdoutLines.Enqueue);

        // 'wait' mode prints the marker line then sleeps forever — wait for the marker
        // to confirm the env var reached the child.
        await WaitForLineAsync(stdoutLines, "mode=wait", TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Kill_terminates_a_long_running_process()
    {
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>
            {
                ["HUSKY_TESTAPP_MODE"] = "wait",
            });

        ConcurrentQueue<string> stdoutLines = new();
        await using AppProcess app = AppProcess.Start(options, onStandardOutput: stdoutLines.Enqueue);

        await WaitForLineAsync(stdoutLines, "testapp: ready", TimeSpan.FromSeconds(10));

        Assert.False(app.HasExited);
        app.Kill();

        await app.ExitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(app.HasExited);
    }

    [Fact]
    public async Task DisposeAsync_kills_a_running_process()
    {
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>
            {
                ["HUSKY_TESTAPP_MODE"] = "wait",
            });

        ConcurrentQueue<string> stdoutLines = new();
        AppProcess app = AppProcess.Start(options, onStandardOutput: stdoutLines.Enqueue);

        await WaitForLineAsync(stdoutLines, "testapp: ready", TimeSpan.FromSeconds(10));

        await app.DisposeAsync();

        Assert.True(app.ExitTask.IsCompleted);
    }

    [Fact]
    public void Start_throws_FileNotFoundException_when_executable_is_missing()
    {
        AppProcessOptions options = new(
            ExecutablePath: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"),
            WorkingDirectory: Path.GetTempPath());

        Assert.Throws<FileNotFoundException>(() => AppProcess.Start(options));
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        AppProcessOptions options = new(
            ExecutablePath: TestAppLocator.ResolvePath(),
            WorkingDirectory: TestAppLocator.ResolveDirectory(),
            Environment: new Dictionary<string, string?>());

        AppProcess app = AppProcess.Start(options);

        await app.DisposeAsync();
        await app.DisposeAsync(); // must not throw
    }

    private static async Task WaitForLineAsync(
        ConcurrentQueue<string> lines, string fragment, TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (lines.Any(line => line.Contains(fragment, StringComparison.Ordinal))) return;
            try { await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token); }
            catch (OperationCanceledException) { break; }
        }

        throw new TimeoutException(
            $"Did not see a line containing '{fragment}' within {timeout}. Got: [{string.Join("; ", lines)}]");
    }
}
