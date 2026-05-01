namespace Husky.Tests.EndToEnd;

internal static class LauncherLocator
{
    public static string ResolvePath()
    {
        // AppContext.BaseDirectory looks like:
        //   .../tests/Husky.Tests/bin/<Configuration>/net10.0
        DirectoryInfo testBase = new(AppContext.BaseDirectory);
        string config = testBase.Parent?.Name
            ?? throw new InvalidOperationException(
                $"Could not derive build configuration from '{AppContext.BaseDirectory}'.");
        DirectoryInfo testsRoot = testBase.Parent!.Parent!.Parent!.Parent!;
        DirectoryInfo solutionRoot = testsRoot.Parent
            ?? throw new InvalidOperationException(
                $"Could not derive solution root from '{testsRoot.FullName}'.");

        string fileName = OperatingSystem.IsWindows() ? "Husky.exe" : "Husky";
        string path = Path.Combine(
            solutionRoot.FullName, "src", "Husky", "bin", config, "net10.0", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Husky launcher binary not found at '{path}'. Make sure the solution has been built.",
                path);

        return path;
    }

    public static string ResolveDirectory() => Path.GetDirectoryName(ResolvePath())!;
}
