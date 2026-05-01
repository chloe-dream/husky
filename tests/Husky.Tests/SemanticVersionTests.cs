using Husky;

namespace Husky.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V1.2.3", 1, 2, 3, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1", 1, 0, 0, "")]
    [InlineData("1.2.3-beta", 1, 2, 3, "beta")]
    [InlineData("1.2.3-rc.1", 1, 2, 3, "rc.1")]
    [InlineData("1.2.3+build.7", 1, 2, 3, "")]
    [InlineData("1.2.3-rc.1+build.7", 1, 2, 3, "rc.1")]
    [InlineData("  v1.2.3  ", 1, 2, 3, "")]
    public void Parse_accepts_well_formed_versions(string input, int major, int minor, int patch, string preRelease)
    {
        SemanticVersion version = SemanticVersion.Parse(input);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("1.x")]
    [InlineData("1.2.3.4")]
    [InlineData("1..2")]
    public void TryParse_returns_false_on_garbage(string? input)
    {
        Assert.False(SemanticVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void Compare_orders_versions_numerically(string left, string right, int expectedSign)
    {
        SemanticVersion a = SemanticVersion.Parse(left);
        SemanticVersion b = SemanticVersion.Parse(right);
        Assert.Equal(expectedSign, Math.Sign(a.CompareTo(b)));
        Assert.Equal(-expectedSign, Math.Sign(b.CompareTo(a)));
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.2", -1)]
    public void Compare_handles_pre_release_per_semver_rules(string left, string right, int expectedSign)
    {
        SemanticVersion a = SemanticVersion.Parse(left);
        SemanticVersion b = SemanticVersion.Parse(right);
        Assert.Equal(expectedSign, Math.Sign(a.CompareTo(b)));
    }

    [Fact]
    public void Build_metadata_does_not_affect_comparison()
    {
        SemanticVersion a = SemanticVersion.Parse("1.2.3+build.1");
        SemanticVersion b = SemanticVersion.Parse("1.2.3+build.999");
        Assert.Equal(0, a.CompareTo(b));
    }
}
