namespace Husky;

internal sealed record UpdateInfo(
    string Version,
    Uri DownloadUrl,
    string? Sha256);
