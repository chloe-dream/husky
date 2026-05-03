using System.Net;
using Husky;
using Husky.Tests.Fixtures;

namespace Husky.Tests;

public sealed class HttpUpdateSourceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_returns_update_when_manifest_is_newer()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            {
              "version": "1.4.3",
              "url": "https://example.test/UmbrellaBot-1.4.3.zip",
              "sha256": "9b74c9897bac770ffc029102a200c5de"
            }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.4.2", CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("1.4.3", update!.Version);
        Assert.Equal("https://example.test/UmbrellaBot-1.4.3.zip", update.DownloadUrl.ToString());
        Assert.Equal("9b74c9897bac770ffc029102a200c5de", update.Sha256);
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_null_when_manifest_is_same_or_older()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            { "version": "1.0.0", "url": "https://example.test/p.zip" }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        Assert.Null(await source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
        Assert.Null(await source.CheckForUpdateAsync("1.0.1", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_null_when_sha256_field_is_absent()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            { "version": "2.0.0", "url": "https://example.test/p.zip" }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);
        Assert.NotNull(update);
        Assert.Null(update!.Sha256);
    }

    [Fact]
    public async Task CheckForUpdateAsync_throws_when_manifest_lacks_required_fields()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/no-version.json", """{ "url": "https://x/" }""");
        server.MapJson("/no-url.json", """{ "version": "1.0.0" }""");

        using HttpClient http = new();

        HttpUpdateSource a = new(http, server.Url("no-version.json"), "0.1.0");
        UpdateException ex1 = await Assert.ThrowsAsync<UpdateException>(() =>
            a.CheckForUpdateAsync("0.0.1", CancellationToken.None));
        Assert.Contains("version", ex1.Message, StringComparison.OrdinalIgnoreCase);

        HttpUpdateSource b = new(http, server.Url("no-url.json"), "0.1.0");
        UpdateException ex2 = await Assert.ThrowsAsync<UpdateException>(() =>
            b.CheckForUpdateAsync("0.0.1", CancellationToken.None));
        Assert.Contains("url", ex2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdateAsync_throws_on_404()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapStatus("/missing.json", (int)HttpStatusCode.NotFound);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("missing.json"), "0.1.0");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            source.CheckForUpdateAsync("1.0.0", CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_sends_user_agent()
    {
        string? capturedUserAgent = null;
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.Map("/manifest.json", req =>
        {
            capturedUserAgent = req.Headers["User-Agent"];
            return new RouteResponse(200, "application/json",
                System.Text.Encoding.UTF8.GetBytes("""
                    { "version": "1.0.0", "url": "http://x/" }
                    """));
        });

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");
        await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.Contains("Husky/0.1.0", capturedUserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdateAsync_populates_source_supplied_config_from_manifest()
    {
        // LEASH §9.3 — manifest's optional `config:` block carries deployment
        // metadata (name, executable, timing knobs) that the launcher merges
        // with local config per §5.2.
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            {
              "version": "1.4.3",
              "url": "https://example.test/UmbrellaBot-1.4.3.zip",
              "config": {
                "name": "umbrella-bot",
                "executable": "app/UmbrellaBot.exe",
                "checkMinutes": 30,
                "shutdownTimeoutSec": 90
              }
            }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.4.2", CancellationToken.None);

        Assert.NotNull(update?.Config);
        Assert.Equal("umbrella-bot", update!.Config!.Name);
        Assert.Equal("app/UmbrellaBot.exe", update.Config.Executable);
        Assert.Equal(30, update.Config.CheckMinutes);
        Assert.Equal(90, update.Config.ShutdownTimeoutSec);
        Assert.False(update.SourceFieldDropped);
    }

    [Fact]
    public async Task CheckForUpdateAsync_flags_source_field_dropped_when_present_in_config()
    {
        // Anti-redirect: a source-supplied config must never carry its own
        // `source` block. The provider drops it and signals SourceFieldDropped
        // so the launcher can warn the app author.
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            {
              "version": "1.0.0",
              "url": "https://example.test/p.zip",
              "config": {
                "name": "x",
                "executable": "x.exe",
                "source": { "type": "github", "repo": "evil/redirect" }
              }
            }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("0.9.0", CancellationToken.None);

        Assert.NotNull(update?.Config);
        Assert.True(update!.SourceFieldDropped);
    }

    [Fact]
    public async Task CheckForUpdateAsync_returns_null_config_when_manifest_omits_block()
    {
        await using FakeHttpServer server = FakeHttpServer.StartEmpty();
        server.MapJson("/manifest.json", """
            { "version": "2.0.0", "url": "https://example.test/p.zip" }
            """);

        using HttpClient http = new();
        HttpUpdateSource source = new(http, server.Url("manifest.json"), "0.1.0");

        UpdateInfo? update = await source.CheckForUpdateAsync("1.0.0", CancellationToken.None);

        Assert.NotNull(update);
        Assert.Null(update!.Config);
        Assert.False(update.SourceFieldDropped);
    }
}
