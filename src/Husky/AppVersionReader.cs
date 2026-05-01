using System.Diagnostics;

namespace Husky;

internal static class AppVersionReader
{
    /// <summary>
    /// Per LEASH §5.3.3: read the version from the executable's
    /// FileVersionInfo. If the executable is absent, return "0.0.0" so
    /// bootstrap mode treats any source version as newer.
    /// </summary>
    public const string BootstrapVersion = "0.0.0";

    public static string ReadCurrent(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
            return BootstrapVersion;

        FileVersionInfo info = FileVersionInfo.GetVersionInfo(executablePath);
        string? fileVersion = info.FileVersion;
        return string.IsNullOrWhiteSpace(fileVersion) ? BootstrapVersion : fileVersion;
    }
}
