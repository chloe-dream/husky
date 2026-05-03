namespace Husky.Client;

public sealed record HuskyUpdateInfo(
    string CurrentVersion,
    string NewVersion,
    long? DownloadSizeBytes);
