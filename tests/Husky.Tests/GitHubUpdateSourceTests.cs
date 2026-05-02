using System.Net;
using Husky;
using Husky.Tests.Fixtures;

namespace Husky.Tests;

public sealed class GitHubUpdateSourceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_returns_update_when_remote_version_is_newer()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/chloe/umbrella/releases/latest", """
            {
              "tag_name": "v1.4.3",
              "draft": false,
              "prerelease": false,
              "assets": [
                { "name": "UmbrellaBot-1.4.3.zip",
                  "browser_download_url": "https://example.test/UmbrellaBot-1.4.3.zip" }
              ]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "chloe/umbrella", "UmbrellaBot-{version}.zip", "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.4.2", CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("1.4.3", update!.Version);
        Assert.Equal("https://example.test/UmbrellaBot-1.4.3.zip", update.DownloadUrl.ToString());
        Assert.Null(update.Sha256);
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_null_when_remote_is_same_or_older()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "1.0.0",
              "assets": [{ "name": "pkg-1.0.0.zip", "browser_download_url": "http://localhost/pkg-1.0.0.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
        Assert.Null(await source.CheckForUpdateAsync("1.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_strips_v_prefix_from_tag()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "v2.0.0",
              "assets": [{ "name": "pkg-2.0.0.zip", "browser_download_url": "http://localhost/pkg-2.0.0.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);
        Assert.Equal("2.0.0", update!.Version);
    }

    [Fact]
    public async Task CheckForUpdateAsync_throws_when_asset_pattern_does_not_match()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "1.1.0",
              "assets": [{ "name": "OtherName.zip", "browser_download_url": "http://localhost/Other.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "Bot-{version}.zip", "0.1.0");

        UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() =>
            source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
        Assert.Contains("Bot-1.1.0.zip", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdateAsync_skips_prereleases_by_default()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "v2.0.0",
              "prerelease": true,
              "assets": [{ "name": "pkg-2.0.0.zip", "browser_download_url": "http://localhost/pkg.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_skips_semver_prerelease_tags_by_default()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "v2.0.0-rc.1",
              "prerelease": false,
              "assets": [{ "name": "pkg-2.0.0-rc.1.zip", "browser_download_url": "http://localhost/pkg.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_prerelease_when_explicitly_allowed()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "v2.0.0-beta",
              "prerelease": true,
              "assets": [{ "name": "pkg-2.0.0-beta.zip", "browser_download_url": "http://localhost/pkg.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(
            http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0", allowPreRelease: true);

        UpdateInfo? update = await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);
        Assert.NotNull(update);
        Assert.Equal("2.0.0-beta", update!.Version);
    }

    [Fact]
    public async Task CheckForUpdateAsync_skips_drafts()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/repos/x/y/releases/latest", """
            {
              "tag_name": "v9.9.9",
              "draft": true,
              "assets": [{ "name": "pkg-9.9.9.zip", "browser_download_url": "http://localhost/pkg.zip" }]
            }
            """);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_throws_on_http_error()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapStatus("/repos/x/y/releases/latest", (int)HttpStatusCode.NotFound);

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_sends_user_agent_with_launcher_version()
    {
        string? capturedUserAgent = null;
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.Map("/repos/x/y/releases/latest", req =>
        {
            capturedUserAgent = req.Headers["User-Agent"];
            return new RouteResponse(200, "application/json",
                System.Text.Encoding.UTF8.GetBytes("""
                    { "tag_name": "1.0.0", "assets": [] }
                    """));
        });

        using HttpClient http = new();
        GitHubUpdateSource source = new(http, server.Address, "x/y", "pkg-{version}.zip", "0.1.0");

        await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.Contains("Husky/0.1.0", capturedUserAgent, StringComparison.Ordinal);
    }
}
