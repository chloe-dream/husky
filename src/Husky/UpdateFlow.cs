namespace Husky;

internal sealed class UpdateFlow(
    UpdateDownloader downloader,
    string installDirectory,
    string executableRelativePath) : IDisposable
{
    private readonly SemaphoreSlim updateLock = new(initialCount: 1, maxCount: 1);

    public void Dispose() => updateLock.Dispose();

    /// <summary>
    /// Phase 1 (LEASH §7.2) followed by phase 2 (§7.3). The launcher provides
    /// <paramref name="stopAppAsync"/> (graceful stop) and
    /// <paramref name="startAppAndAwaitHelloAsync"/> (start the new app and
    /// wait for its hello). For bootstrap mode, <paramref name="stopAppAsync"/>
    /// should be a no-op.
    /// </summary>
    public async Task RunAsync(
        UpdateInfo update,
        Func<CancellationToken, Task> stopAppAsync,
        Func<CancellationToken, Task> startAppAndAwaitHelloAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(stopAppAsync);
        ArgumentNullException.ThrowIfNull(startAppAndAwaitHelloAsync);

        if (!await updateLock.WaitAsync(0, ct).ConfigureAwait(false))
            throw new UpdateException("Another update is already running.");

        try
        {
            string downloadDir = Path.Combine(installDirectory, "download");
            string extractedDir = Path.Combine(downloadDir, "extracted");

            // Phase 1 — preparation; app keeps running.
            ClearDirectory(downloadDir);

            string assetFileName = DeriveAssetFileName(update);
            string zipPath = Path.Combine(downloadDir, assetFileName);

            await downloader.DownloadAsync(update.DownloadUrl, update.Sha256, zipPath, ct).ConfigureAwait(false);
            UpdateExtractor.Extract(zipPath, extractedDir, executableRelativePath);

            // Phase 2 — cutover.
            await stopAppAsync(ct).ConfigureAwait(false);
            UpdateInstaller.Install(extractedDir, installDirectory);
            await startAppAndAwaitHelloAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            updateLock.Release();
        }
    }

    private static string DeriveAssetFileName(UpdateInfo update)
    {
        string fromUrl = Path.GetFileName(update.DownloadUrl.LocalPath);
        if (!string.IsNullOrEmpty(fromUrl)) return fromUrl;
        return $"package-{update.Version}.zip";
    }

    private static void ClearDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            return;
        }

        foreach (string dir in Directory.EnumerateDirectories(path))
            Directory.Delete(dir, recursive: true);
        foreach (string file in Directory.EnumerateFiles(path))
            File.Delete(file);
    }
}
