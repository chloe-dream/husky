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

        // The .NET apphost is stamped with FileVersion on Windows but NOT on
        // Linux/macOS — so reading the apphost directly returns nothing on
        // those platforms. The managed assembly (.dll) next to the apphost is
        // always stamped with AssemblyFileVersion, so prefer it when present.
        // Single-file published binaries have no sibling .dll; the apphost
        // itself is the bundle and carries the version, so we fall back to it.
        string? managedAssembly = TryFindManagedAssembly(executablePath);
        if (managedAssembly is not null)
        {
            string? fromDll = FileVersionInfo.GetVersionInfo(managedAssembly).FileVersion;
            if (!string.IsNullOrWhiteSpace(fromDll)) return fromDll;
        }

        string? fromExe = FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
        return string.IsNullOrWhiteSpace(fromExe) ? BootstrapVersion : fromExe;
    }

    private static string? TryFindManagedAssembly(string executablePath)
    {
        string? directory = Path.GetDirectoryName(executablePath);
        if (directory is null) return null;

        string fileName = Path.GetFileName(executablePath);
        if (string.IsNullOrEmpty(fileName)) return null;

        // The Linux/macOS apphost has no extension and may contain dots in
        // its name (e.g. "Husky.TestApp"). Path.GetFileNameWithoutExtension
        // would strip the trailing segment and produce the wrong sibling
        // name, so handle the two layouts explicitly.
        string stem = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        string candidate = Path.Combine(directory, stem + ".dll");
        return File.Exists(candidate) ? candidate : null;
    }
}
