using Husky;

namespace Husky.Tests;

public sealed class AppVersionReaderTests
{
    [Fact]
    public void ReadCurrent_returns_bootstrap_version_when_file_does_not_exist()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        string version = AppVersionReader.ReadCurrent(missing);

        Assert.Equal(AppVersionReader.BootstrapVersion, version);
    }

    [Fact]
    public void ReadCurrent_returns_FileVersion_for_an_existing_executable()
    {
        // The TestApp binary always carries a FileVersion (default 1.0.0.0).
        string version = AppVersionReader.ReadCurrent(TestAppLocator.ResolvePath());

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.NotEqual(AppVersionReader.BootstrapVersion, version);
    }

    [Fact]
    public void ReadCurrent_throws_when_path_is_blank()
    {
        Assert.Throws<ArgumentException>(() => AppVersionReader.ReadCurrent("  "));
    }
}
