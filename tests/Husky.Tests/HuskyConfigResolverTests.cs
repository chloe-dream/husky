using Husky;

namespace Husky.Tests;

public sealed class HuskyConfigResolverTests
{
    private static readonly SourceConfig GitHubSource = new(
        Type: SourceConfig.GitHubType,
        Repo: "x/y",
        Asset: "y-{version}.zip");

    [Fact]
    public void Resolve_applies_built_in_defaults_when_neither_local_nor_supplied_set_a_field()
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "x.exe");

        HuskyConfig effective = HuskyConfigResolver.Resolve(local, supplied: null);

        Assert.Equal(HuskyConfig.DefaultCheckMinutes, effective.CheckMinutes);
        Assert.Equal(HuskyConfig.DefaultShutdownTimeoutSec, effective.ShutdownTimeoutSec);
        Assert.Equal(HuskyConfig.DefaultKillAfterSec, effective.KillAfterSec);
        Assert.Equal(HuskyConfig.DefaultRestartAttempts, effective.RestartAttempts);
        Assert.Equal(HuskyConfig.DefaultRestartPauseSec, effective.RestartPauseSec);
    }

    [Fact]
    public void Resolve_takes_name_and_executable_from_source_supplied_when_local_omits_them()
    {
        // The "minimal local config" story: user only supplies source, app
        // author owns name + executable in source-supplied config.
        LocalHuskyConfig local = new(Source: GitHubSource);
        SourceSuppliedConfig supplied = new(
            Name: "fishbowl",
            Executable: "Fishbowl.exe");

        HuskyConfig effective = HuskyConfigResolver.Resolve(local, supplied);

        Assert.Equal("fishbowl", effective.Name);
        Assert.Equal("Fishbowl.exe", effective.Executable);
    }

    [Fact]
    public void Resolve_prefers_local_value_over_source_supplied_value()
    {
        // Local override semantics: a power user can override what the
        // source-supplied config carries, per LEASH §5.2 precedence.
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "user-override",
            CheckMinutes: 15);
        SourceSuppliedConfig supplied = new(
            Name: "supplied-name",
            Executable: "app.exe",
            CheckMinutes: 60);

        HuskyConfig effective = HuskyConfigResolver.Resolve(local, supplied);

        Assert.Equal("user-override", effective.Name);
        Assert.Equal("app.exe", effective.Executable); // local omitted, supplied wins
        Assert.Equal(15, effective.CheckMinutes);     // local override wins
    }

    [Fact]
    public void Resolve_keeps_source_block_from_local_only_never_replaced_by_supplied()
    {
        // Anti-redirect: SourceSuppliedConfig has no Source field anyway,
        // but we lock in the local source up front regardless.
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "x.exe");

        HuskyConfig effective = HuskyConfigResolver.Resolve(local, supplied: new SourceSuppliedConfig(Name: "x"));

        Assert.Same(GitHubSource, effective.Source);
    }

    [Fact]
    public void Resolve_throws_when_name_is_unresolved_after_merge()
    {
        LocalHuskyConfig local = new(Source: GitHubSource, Executable: "x.exe");

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_throws_when_executable_is_unresolved_after_merge()
    {
        LocalHuskyConfig local = new(Source: GitHubSource, Name: "x");

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("executable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_rejects_absolute_executable_paths()
    {
        // LEASH §5.2: executable must be relative to launcher dir.
        string absolute = OperatingSystem.IsWindows()
            ? "C:\\Program Files\\App\\app.exe"
            : "/opt/app/app";

        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: absolute);

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("relative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_rejects_traversal_in_executable_path()
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "../escape/app.exe");

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("..", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_normalizes_backslashes_to_forward_slashes()
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "app\\sub\\x.exe");

        HuskyConfig effective = HuskyConfigResolver.Resolve(local, supplied: null);

        Assert.Equal("app/sub/x.exe", effective.Executable);
    }

    [Fact]
    public void Resolve_throws_when_check_minutes_below_minimum()
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "x.exe",
            CheckMinutes: 1);

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("checkMinutes", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Resolve_throws_when_shutdown_timeout_is_non_positive(int value)
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "x.exe",
            ShutdownTimeoutSec: value);

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains("shutdownTimeoutSec", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("killAfterSec", -1)]
    [InlineData("restartAttempts", -1)]
    [InlineData("restartPauseSec", -1)]
    public void Resolve_throws_when_non_negative_field_is_negative(string field, int value)
    {
        LocalHuskyConfig local = new(
            Source: GitHubSource,
            Name: "x",
            Executable: "x.exe",
            KillAfterSec: field == "killAfterSec" ? value : null,
            RestartAttempts: field == "restartAttempts" ? value : null,
            RestartPauseSec: field == "restartPauseSec" ? value : null);

        HuskyConfigException ex = Assert.Throws<HuskyConfigException>(
            () => HuskyConfigResolver.Resolve(local, supplied: null));

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
    }
}
