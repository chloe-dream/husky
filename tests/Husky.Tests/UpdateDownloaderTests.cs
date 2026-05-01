using Husky;
using Husky.Tests.Fixtures;

namespace Husky.Tests;

public sealed class UpdateDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_writes_payload_to_target_path()
    {
        using TempDirectory root = TempDirectory.Create();
        string source = Path.Combine(root.Path, "package.zip");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string target = Path.Combine(root.Path, "out.zip");

        await downloader.DownloadAsync(server.Url("package.zip"), expectedSha256: null, target);

        Assert.True(File.Exists(target));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task DownloadAsync_succeeds_when_sha256_matches()
    {
        using TempDirectory root = TempDirectory.Create();
        string source = Path.Combine(root.Path, "package.zip");
        await File.WriteAllBytesAsync(source, [10, 20, 30]);
        string expected = FakeReleaseBuilder.Sha256(source);

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string target = Path.Combine(root.Path, "out.zip");

        await downloader.DownloadAsync(server.Url("package.zip"), expected, target);

        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task DownloadAsync_throws_and_deletes_target_when_sha256_mismatches()
    {
        using TempDirectory root = TempDirectory.Create();
        string source = Path.Combine(root.Path, "package.zip");
        await File.WriteAllBytesAsync(source, [10, 20, 30]);

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string target = Path.Combine(root.Path, "out.zip");

        UpdateException ex = await Assert.ThrowsAsync<UpdateException>(async () =>
            await downloader.DownloadAsync(server.Url("package.zip"), expectedSha256: "deadbeef", target));

        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task DownloadAsync_throws_on_404()
    {
        using TempDirectory root = TempDirectory.Create();

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string target = Path.Combine(root.Path, "out.zip");

        UpdateException ex = await Assert.ThrowsAsync<UpdateException>(async () =>
            await downloader.DownloadAsync(server.Url("missing.zip"), expectedSha256: null, target));

        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_creates_intermediate_directories()
    {
        using TempDirectory root = TempDirectory.Create();
        string source = Path.Combine(root.Path, "package.zip");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string nested = Path.Combine(root.Path, "download", "out.zip");

        await downloader.DownloadAsync(server.Url("package.zip"), expectedSha256: null, nested);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task DownloadAsync_is_case_insensitive_about_the_expected_sha256_string()
    {
        using TempDirectory root = TempDirectory.Create();
        string source = Path.Combine(root.Path, "package.zip");
        await File.WriteAllBytesAsync(source, [10, 20, 30]);
        string expected = FakeReleaseBuilder.Sha256(source).ToUpperInvariant();

        await using FakeHttpServer server = FakeHttpServer.Start(root.Path);

        using HttpClient client = new();
        UpdateDownloader downloader = new(client);
        string target = Path.Combine(root.Path, "out.zip");

        await downloader.DownloadAsync(server.Url("package.zip"), expected, target);

        Assert.True(File.Exists(target));
    }
}
