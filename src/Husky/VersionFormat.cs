namespace Husky;

internal static class VersionFormat
{
    /// <summary>
    /// Drop the build-number bits — the 4th FileVersion component
    /// (<c>X.Y.Z.W</c>) and SemVer build metadata (<c>+commit</c>) — so
    /// every user-visible version reads as <c>X.Y.Z</c>. A pre-release tag
    /// (<c>-rc.1</c>, <c>-beta</c>) is preserved because it marks a release
    /// channel, not a build. Falls through unchanged for inputs the SemVer
    /// parser rejects.
    /// </summary>
    internal static string ToDisplay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
        if (!SemanticVersion.TryParse(raw, out SemanticVersion v)) return raw;
        return v.PreRelease.Length > 0
            ? $"{v.Major}.{v.Minor}.{v.Patch}-{v.PreRelease}"
            : $"{v.Major}.{v.Minor}.{v.Patch}";
    }
}
