namespace Husky.Tests;

internal static class TestAppLocator
{
    /// <summary>
    /// Resolves the absolute path to the Husky.TestApp binary built alongside
    /// the test assembly. Husky.Tests references Husky.TestApp via
    /// <c>ProjectReference ReferenceOutputAssembly="false"</c>, so the binary
    /// is guaranteed to be built but is not copied into the test output dir.
    /// </summary>
    public static string ResolvePath()
    {
        // AppContext.BaseDirectory looks like:
        //   .../tests/Husky.Tests/bin/<Configuration>/net10.0
        DirectoryInfo testBase = new(AppContext.BaseDirectory);
        string config = testBase.Parent?.Name
            ?? throw new InvalidOperationException(
                $"Could not derive build configuration from '{AppContext.BaseDirectory}'.");
        DirectoryInfo testsRoot = testBase.Parent!.Parent!.Parent!.Parent!;

        string fileName = OperatingSystem.IsWindows() ? "Husky.TestApp.exe" : "Husky.TestApp";
        string path = Path.Combine(
            testsRoot.FullName, "Husky.TestApp", "bin", config, "net10.0", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"TestApp binary not found at '{path}'. Make sure the solution has been built.",
                path);

        return path;
    }

    public static string ResolveDirectory() => Path.GetDirectoryName(ResolvePath())!;
}
