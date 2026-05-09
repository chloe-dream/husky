namespace Husky.Tests.Fixtures;

/// <summary>
/// Convenience presets over <see cref="FakeReleaseBuilder"/>.
/// </summary>
internal static class FakeRelease
{
    /// <summary>
    /// A package shaped like a real Husky-managed release: an executable at
    /// <c>app/Husky.TestApp.exe</c> plus a couple of inert companions.
    /// </summary>
    public static FakeReleaseBuilder Valid(
        string executableRelativePath = "app/Husky.TestApp.exe",
        string version = "1.0.0") =>
        new FakeReleaseBuilder()
            .WithExecutable(executableRelativePath, $"fake-executable v{version}")
            .WithFile("app/release.notes", $"version {version}");

    /// <summary>
    /// Same shape as <see cref="Valid"/> but missing the executable. Used to
    /// exercise the extractor's sanity check (LEASH §7.2 step 5).
    /// </summary>
    public static FakeReleaseBuilder MissingExecutable() =>
        new FakeReleaseBuilder()
            .WithFile("app/release.notes", "no exe here")
            .WithFile("app/data/some.txt", "still no exe");

    /// <summary>
    /// Executable is at the wrong path — root rather than under <c>app/</c>.
    /// </summary>
    public static FakeReleaseBuilder WrongStructure() =>
        new FakeReleaseBuilder()
            .WithExecutable("Husky.TestApp.exe");
}
