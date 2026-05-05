using System.IO.Compression;
using Retro.Crt;

namespace Husky;

internal static class UpdateExtractor
{
    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <paramref name="targetDirectory"/>
    /// (which is wiped first), then verifies the package contains the expected
    /// executable. Implements LEASH §7.2 steps 4-5.
    /// </summary>
    public static void Extract(
        string zipPath,
        string targetDirectory,
        string expectedExecutableRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExecutableRelativePath);

        if (!File.Exists(zipPath))
            throw new UpdateException($"Update package not found: '{zipPath}'.");

        if (Directory.Exists(targetDirectory))
            Directory.Delete(targetDirectory, recursive: true);
        Directory.CreateDirectory(targetDirectory);

        // ZipFile.ExtractToDirectory is synchronous with no progress hook. A
        // spinner gives the user a "still working" cue and stays in the LEASH
        // §10.4 voice on its way out.
        using (ConsoleOutput.BeginLiveWidget())
        {
            using var spinner = Spinner.Show("extracting", SpinnerStyle.Braille, Color.LightCyan);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, targetDirectory);
                spinner.Stop();
                ConsoleOutput.Husky("extracted.", messageColor: Color.LightGreen);
            }
            catch (InvalidDataException ex)
            {
                spinner.Stop();
                ConsoleOutput.Husky("extract failed.", messageColor: Color.LightRed);
                throw new UpdateException($"Update package is corrupt: {ex.Message}", ex);
            }
        }

        string expectedAbsolute = Path.Combine(
            targetDirectory,
            expectedExecutableRelativePath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(expectedAbsolute))
            throw new UpdateException(
                $"Update package is missing the expected executable at '{expectedExecutableRelativePath}'.");
    }
}
