namespace Husky.Protocol;

public sealed record UpdateStatusPayload(
    bool Available,
    string CurrentVersion,
    string? NewVersion = null,
    long? DownloadSizeBytes = null);
