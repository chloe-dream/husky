using Husky;
using Husky.Tests.Fixtures;

namespace Husky.Tests;

public sealed class UpdateInstallerTests
{
    [Fact]
    public void Install_copies_all_files_from_source_to_target()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "src");
        string target = Path.Combine(work.Path, "dst");

        WriteFile(source, "app/exe", "v1");
        WriteFile(source, "app/lib/a.dll", "alpha");
        WriteFile(source, "app/lib/b.dll", "beta");

        UpdateInstaller.Install(source, target);

        Assert.Equal("v1", File.ReadAllText(Path.Combine(target, "app", "exe")));
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(target, "app", "lib", "a.dll")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(target, "app", "lib", "b.dll")));
    }

    [Fact]
    public void Install_overwrites_existing_files_in_the_target()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "src");
        string target = Path.Combine(work.Path, "dst");

        WriteFile(target, "app/exe", "old version");
        WriteFile(source, "app/exe", "new version");

        UpdateInstaller.Install(source, target);

        Assert.Equal("new version", File.ReadAllText(Path.Combine(target, "app", "exe")));
    }

    [Fact]
    public void Install_leaves_files_outside_the_update_untouched()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "src");
        string target = Path.Combine(work.Path, "dst");

        // Pre-existing user data — the update must NOT touch this.
        WriteFile(target, "app/data/user.db", "user data");
        WriteFile(target, "app/data/cache/x.bin", "cache");
        WriteFile(target, "app/exe", "old exe");

        // Update only ships an updated exe.
        WriteFile(source, "app/exe", "new exe");

        UpdateInstaller.Install(source, target);

        Assert.Equal("user data", File.ReadAllText(Path.Combine(target, "app", "data", "user.db")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(target, "app", "data", "cache", "x.bin")));
        Assert.Equal("new exe", File.ReadAllText(Path.Combine(target, "app", "exe")));
    }

    [Fact]
    public void Install_does_not_delete_files_present_in_old_version_but_not_in_update()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "src");
        string target = Path.Combine(work.Path, "dst");

        // Old version had this dll; new version drops it.
        WriteFile(target, "app/lib/legacy.dll", "old code");
        WriteFile(target, "app/exe", "old exe");

        // Update has only the new exe.
        WriteFile(source, "app/exe", "new exe");

        UpdateInstaller.Install(source, target);

        Assert.True(File.Exists(Path.Combine(target, "app", "lib", "legacy.dll")));
    }

    [Fact]
    public void Install_creates_target_directory_if_it_does_not_exist()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "src");
        string target = Path.Combine(work.Path, "fresh-target");

        WriteFile(source, "app/exe", "hello");

        UpdateInstaller.Install(source, target);

        Assert.True(File.Exists(Path.Combine(target, "app", "exe")));
    }

    [Fact]
    public void Install_throws_when_source_directory_does_not_exist()
    {
        using TempDirectory work = TempDirectory.Create();
        string source = Path.Combine(work.Path, "missing");
        string target = Path.Combine(work.Path, "dst");

        UpdateException ex = Assert.Throws<UpdateException>(() =>
            UpdateInstaller.Install(source, target));

        Assert.Contains("source", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        string full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
