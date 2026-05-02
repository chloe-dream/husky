using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Husky.Tests.Fixtures;

namespace Husky.Tests.EndToEnd;

/// <summary>
/// Boot a launcher with no installed app, point its source at a real
/// FakeHttpServer that hands back a ZIP of the TestApp build, and assert
/// that the launcher bootstraps the app and brings it up.
/// </summary>
public sealed class BootstrapEndToEndTests
{
    [Fact]
    public async Task Launcher_bootstraps_TestApp_from_an_http_manifest()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        File.Delete(staged.AppExecutablePath); // empty app directory triggers bootstrap.

        using TempDirectory releases = TempDirectory.Create("husky-bootstrap-releases");
        string zipPath = BuildTestAppZip(releases.Path, "TestApp-1.0.0.zip");
        string sha256 = FakeReleaseBuilder.Sha256(zipPath);

        await using FakeHttpServer server = FakeHttpServer.Start(releases.Path);
        Uri zipUrl = server.Url("TestApp-1.0.0.zip");

        string manifestJson = $$"""
            {
              "version": "1.0.0",
              "url": "{{zipUrl}}",
              "sha256": "{{sha256}}"
            }
            """;
        server.MapJson("/manifest.json", manifestJson);
        Uri manifestUrl = server.Url("manifest.json");

        staged.WriteDefaultConfig(name: "bootstrap-app", manifestUrl: manifestUrl.ToString());

        ConcurrentQueue<string> stdoutLines = new();
        System.Diagnostics.Process launcher = staged.Start(onStandardOutput: stdoutLines.Enqueue);

        // Bootstrap should: log the new version, fetch, install, then start.
        await WaitForFragmentAsync(stdoutLines, "bootstrap-app v1.0.0", TimeSpan.FromSeconds(60));

        Assert.Contains(stdoutLines, l => l.Contains("bootstrapping", StringComparison.Ordinal));
        Assert.Contains(stdoutLines, l => l.Contains("new version found", StringComparison.Ordinal));
        Assert.Contains(stdoutLines, l => l.Contains("testapp: ready", StringComparison.Ordinal));

        Assert.True(File.Exists(staged.AppExecutablePath), "TestApp executable should be installed after bootstrap.");
        Assert.False(launcher.HasExited);
    }

    [Fact]
    public async Task Launcher_exits_when_bootstrap_source_returns_no_version()
    {
        await using StagedLauncher staged = StagedLauncher.Create();
        File.Delete(staged.AppExecutablePath);

        using TempDirectory releases = TempDirectory.Create("husky-empty-releases");
        await using FakeHttpServer server = FakeHttpServer.Start(releases.Path);

        // Manifest version 0.0.0 == the bootstrap version → CheckForUpdate
        // returns null → bootstrap throws "no version available".
        server.MapJson("/manifest.json", """
            { "version": "0.0.0", "url": "http://localhost/whatever.zip" }
            """);

        staged.WriteDefaultConfig(manifestUrl: server.Url("manifest.json").ToString());

        ConcurrentQueue<string> stdoutLines = new();
        System.Diagnostics.Process launcher = staged.Start(onStandardOutput: stdoutLines.Enqueue);

        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(2, launcher.ExitCode);
        Assert.Contains(stdoutLines, l => l.Contains("bootstrap failed", StringComparison.Ordinal));
    }

    private static string BuildTestAppZip(string targetDirectory, string zipFileName)
    {
        string zipPath = Path.Combine(targetDirectory, zipFileName);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        string testAppDir = TestAppLocator.ResolveDirectory();
        using FileStream output = File.Create(zipPath);
        using ZipArchive archive = new(output, ZipArchiveMode.Create);

        // The launcher's executable path in the staged config is "app/<exe>",
        // so the ZIP must contain that prefix.
        foreach (string file in Directory.EnumerateFiles(testAppDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(testAppDir, file);
            string entryName = "app/" + relative.Replace('\\', '/');
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
        }

        return zipPath;
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
