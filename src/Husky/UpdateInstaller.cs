namespace Husky;

internal static class UpdateInstaller
{
    /// <summary>
    /// Recursively copies <paramref name="sourceDirectory"/> on top of
    /// <paramref name="targetDirectory"/>. Existing files are overwritten,
    /// existing files that the update does not touch stay untouched, missing
    /// directories are created — and nothing is ever deleted (LEASH §7.3
    /// step 2).
    /// </summary>
    public static void Install(string sourceDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        if (!Directory.Exists(sourceDirectory))
            throw new UpdateException($"Update source directory not found: '{sourceDirectory}'.");

        Directory.CreateDirectory(targetDirectory);

        DirectoryInfo source = new(sourceDirectory);
        foreach (FileInfo file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source.FullName, file.FullName);
            string destination = Path.Combine(targetDirectory, relative);
            string? destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);
            file.CopyTo(destination, overwrite: true);
        }
    }
}
