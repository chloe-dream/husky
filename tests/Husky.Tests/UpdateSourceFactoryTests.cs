using Husky;

namespace Husky.Tests;

public sealed class UpdateSourceFactoryTests
{
    [Fact]
    public void Create_returns_GitHub_source_for_github_type()
    {
        using HttpClient http = new();
        SourceConfig config = new(SourceConfig.GitHubType, Repo: "x/y", Asset: "p-{version}.zip");

        IUpdateSource source = UpdateSourceFactory.Create(config, http, "0.1.0");
        Assert.IsType<GitHubUpdateSource>(source);
    }

    [Fact]
    public void Create_returns_HTTP_source_for_http_type()
    {
        using HttpClient http = new();
        SourceConfig config = new(SourceConfig.HttpType, Manifest: "https://example.test/manifest.json");

        IUpdateSource source = UpdateSourceFactory.Create(config, http, "0.1.0");
        Assert.IsType<HttpUpdateSource>(source);
    }

    [Fact]
    public void Create_throws_when_github_repo_is_missing()
    {
        using HttpClient http = new();
        SourceConfig config = new(SourceConfig.GitHubType, Repo: null, Asset: "p-{version}.zip");

        Assert.Throws<HuskyConfigException>(() => UpdateSourceFactory.Create(config, http, "0.1.0"));
    }

    [Fact]
    public void Create_throws_for_unknown_type()
    {
        using HttpClient http = new();
        SourceConfig config = new(Type: "invented");

        Assert.Throws<HuskyConfigException>(() => UpdateSourceFactory.Create(config, http, "0.1.0"));
    }
}
