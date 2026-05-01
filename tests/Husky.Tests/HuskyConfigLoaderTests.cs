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
    public void Parse_returns_config_with_all_defaults_applied_when_optional_fields_are_omitted()
    {
        HuskyConfig config = HuskyConfigLoader.Parse(ValidGitHubJson);

        Assert.Equal("umbrella-bot", config.Name);
        Assert.Equal("app/UmbrellaBot.exe", config.Executable);
        Assert.Equal(SourceConfig.GitHubType, config.Source.Type);
        Assert.Equal("chloe/umbrella-bot", config.Source.Repo);
        Assert.Equal("UmbrellaBot-{version}.zip", config.Source.Asset);
        Assert.Equal(HuskyConfig.DefaultCheckMinutes, config.CheckMinutes);
        Assert.Equal(HuskyConfig.DefaultShutdownTimeoutSec, config.ShutdownTimeoutSec);
        Assert.Equal(HuskyConfig.DefaultKillAfterSec, config.KillAfterSec);
        Assert.Equal(HuskyConfig.DefaultRestartAttempts, config.RestartAttempts);
        Assert.Equal(HuskyConfig.DefaultRestartPauseSec, config.RestartPauseSec);
    }

    [Fact]
    public void Parse_overrides_defaults_when_optional_fields_are_set()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "github", "repo": "x/y", "asset": "y-{version}.zip" },
              "checkMinutes": 30,
              "shutdownTimeoutSec": 90,
              "killAfterSec": 5,
              "restartAttempts": 5,
              "restartPauseSec": 60
            }
            """;

        HuskyConfig config = HuskyConfigLoader.Parse(json);

        Assert.Equal(30, config.CheckMinutes);
        Assert.Equal(90, config.ShutdownTimeoutSec);
        Assert.Equal(5, config.KillAfterSec);
        Assert.Equal(5, config.RestartAttempts);
        Assert.Equal(60, config.RestartPauseSec);
    }

    [Fact]
    public void Parse_accepts_valid_http_source()
    {
        HuskyConfig config = HuskyConfigLoader.Parse(ValidHttpJson);

        Assert.Equal(SourceConfig.HttpType, config.Source.Type);
        Assert.Equal("https://example.invalid/manifest.json", config.Source.Manifest);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("executable")]
    public void Parse_throws_when_required_top_level_field_is_missing(string fieldToOmit)
    {
        string json = ValidGitHubJson.Replace($"\"{fieldToOmit}\":", $"\"_skip_{fieldToOmit}\":");

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains(fieldToOmit, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_source_is_missing()
    {
        const string json = """
            { "name": "umbrella-bot", "executable": "app/UmbrellaBot.exe" }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("source", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_source_type_is_unknown()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "ftp", "repo": "x/y", "asset": "z" }
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("ftp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_github_source_lacks_repo()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "github", "asset": "z-{version}.zip" }
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("repo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_github_source_lacks_asset()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "github", "repo": "x/y" }
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("asset", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_http_source_lacks_manifest()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "http" }
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("manifest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_http_manifest_is_not_absolute_http_url()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "http", "manifest": "not-a-url" }
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("manifest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_throws_when_checkMinutes_is_below_minimum()
    {
        const string json = """
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "github", "repo": "x/y", "asset": "z-{version}.zip" },
              "checkMinutes": 1
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains("checkMinutes", ex.Message, StringComparison.Ordinal);
        Assert.Contains(HuskyConfig.MinimumCheckMinutes.ToString(), ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shutdownTimeoutSec", 0)]
    [InlineData("shutdownTimeoutSec", -5)]
    [InlineData("killAfterSec", -1)]
    [InlineData("restartAttempts", -1)]
    [InlineData("restartPauseSec", -1)]
    public void Parse_throws_when_numeric_field_is_out_of_range(string field, int value)
    {
        string json = $$"""
            {
              "name": "umbrella-bot",
              "executable": "app/UmbrellaBot.exe",
              "source": { "type": "github", "repo": "x/y", "asset": "z-{version}.zip" },
              "{{field}}": {{value}}
            }
            """;

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigLoader.Parse(json));

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
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
            HuskyConfig config = HuskyConfigLoader.Load(path);

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

        HuskyConfig config = HuskyConfigLoader.Parse(json);

        Assert.Equal("umbrella-bot", config.Name);
    }
}
