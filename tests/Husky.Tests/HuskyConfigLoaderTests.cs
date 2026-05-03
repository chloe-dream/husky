using Husky;

namespace Husky.Tests;

public sealed class HuskyConfigLoaderTests
{
    private const string ValidGitHubJson = """
        {
          "name": "umbrella-bot",
          "executable": "app/UmbrellaBot.exe",
          "source": {
            "type": "github",
            "repo": "chloe/umbrella-bot",
            "asset": "UmbrellaBot-{version}.zip"
          }
        }
        """;

    private const string ValidHttpJson = """
        {
          "name": "umbrella-bot",
          "executable": "app/UmbrellaBot.exe",
          "source": {
            "type": "http",
            "manifest": "https://example.invalid/manifest.json"
          }
        }
        """;

    [Fact]
    public void Parse_returns_a_local_config_with_optional_fields_left_null()
    {
        // Defaults are no longer applied at parse time; HuskyConfigResolver
        // handles that step (LEASH §5.2). Loader's job is JSON + source.
        LocalHuskyConfig config = HuskyConfigLoader.Parse(ValidGitHubJson);

        Assert.Equal("umbrella-bot", config.Name);
        Assert.Equal("app/UmbrellaBot.exe", config.Executable);
        Assert.Equal(SourceConfig.GitHubType, config.Source.Type);
        Assert.Equal("chloe/umbrella-bot", config.Source.Repo);
        Assert.Equal("UmbrellaBot-{version}.zip", config.Source.Asset);
        Assert.False(config.Source.AllowPreRelease);
        Assert.Null(config.CheckMinutes);
        Assert.Null(config.ShutdownTimeoutSec);
        Assert.Null(config.KillAfterSec);
        Assert.Null(config.RestartAttempts);
        Assert.Null(config.RestartPauseSec);
    }

    [Fact]
    public void Parse_carries_optional_overrides_through_to_the_local_config()
    {
        const string json = """
            {
              "source": { "type": "github", "repo": "x/y", "asset": "y-{version}.zip" },
              "checkMinutes": 30,
              "shutdownTimeoutSec": 90,
              "killAfterSec": 5,
              "restartAttempts": 5,
              "restartPauseSec": 60
            }
            """;

        LocalHuskyConfig config = HuskyConfigLoader.Parse(json);

        Assert.Equal(30, config.CheckMinutes);
        Assert.Equal(90, config.ShutdownTimeoutSec);
        Assert.Equal(5, config.KillAfterSec);
        Assert.Equal(5, config.RestartAttempts);
        Assert.Equal(60, config.RestartPauseSec);
    }

    [Fact]
    public void Parse_accepts_a_minimal_local_config_with_only_source()
    {
        // LEASH §5.2: name and executable can come from source-supplied
        // config, so the local file may legitimately omit them.
        const string json = """
            { "source": { "type": "github", "repo": "x/y", "asset": "y-{version}.zip" } }
            """;

        LocalHuskyConfig config = HuskyConfigLoader.Parse(json);

        Assert.Null(config.Name);
        Assert.Null(config.Executable);
        Assert.Equal("x/y", config.Source.Repo);
    }

    [Fact]
    public void Parse_accepts_valid_http_source()
    {
        LocalHuskyConfig config = HuskyConfigLoader.Parse(ValidHttpJson);

        Assert.Equal(SourceConfig.HttpType, config.Source.Type);
        Assert.Equal("https://example.invalid/manifest.json", config.Source.Manifest);
    }

    [Fact]
    public void Parse_reads_allow_pre_release_when_set()
    {
        const string json = """
            {
              "source": {
                "type": "github",
                "repo": "x/y",
                "asset": "x-{version}.zip",
                "allowPreRelease": true
              }
            }
            """;

        LocalHuskyConfig config = HuskyConfigLoader.Parse(json);
        Assert.True(config.Source.AllowPreRelease);
    }

    [Fact]
    public void Parse_throws_when_source_is_missing()
    {
        const string json = """{ "name": "umbrella-bot", "executable": "app/UmbrellaBot.exe" }""";

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("source", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_source_type_is_unknown()
    {
        const string json = """
            { "source": { "type": "ftp", "repo": "x/y", "asset": "z" } }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("ftp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_github_source_lacks_repo()
    {
        const string json = """
            { "source": { "type": "github", "asset": "z-{version}.zip" } }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("repo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_github_source_lacks_asset()
    {
        const string json = """{ "source": { "type": "github", "repo": "x/y" } }""";

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("asset", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_http_source_lacks_manifest()
    {
        const string json = """{ "source": { "type": "http" } }""";

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("manifest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_http_manifest_is_not_absolute_http_url()
    {
        const string json = """
            { "source": { "type": "http", "manifest": "not-a-url" } }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("manifest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_HuskyConfigException_with_inner_JsonException_on_malformed_JSON()
    {
        const string json = "{ this is not valid json";

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
    }

    [Fact]
    public void Parse_throws_when_json_is_the_literal_null()
    {
        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse("null"));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_throws_when_file_does_not_exist()
    {
        string path = Path.Combine(Path.GetTempPath(), $"husky-missing-{Guid.NewGuid():N}.json");

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(() => HuskyConfigLoader.Load(path));

        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_reads_and_parses_a_real_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"husky-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, ValidGitHubJson);
        try
        {
            LocalHuskyConfig config = HuskyConfigLoader.Load(path);

            Assert.Equal("umbrella-bot", config.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_accepts_trailing_commas_and_line_comments()
    {
        const string json = """
            {
              // top-level comment
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": {
                "type": "github",
                "repo": "x/y",
                "asset": "y-{version}.zip", // trailing comma below
              },
            }
            """;

        LocalHuskyConfig config = HuskyConfigLoader.Parse(json);

        Assert.Equal("umbrella-bot", config.Name);
    }
}
