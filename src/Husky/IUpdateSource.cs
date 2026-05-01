namespace Husky;

internal interface IUpdateSource
{
    Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct);
}
