namespace Husky.Protocol;

public sealed record UpdateAvailablePayload(
    string CurrentVersion,
    string NewVersion,
    long? DownloadSizeBytes = null);
