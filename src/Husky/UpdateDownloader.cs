using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Husky;

/// <summary>
/// Sink for streaming-download progress events. The downloader fires
/// <see cref="OnStarted"/> once before the first byte arrives,
/// <see cref="OnAdvanced"/> roughly every 256 KB, and
/// <see cref="OnFinished"/> exactly once after the byte stream completes —
/// i.e. before any post-download verification (SHA-256). A sink that
/// throws from any callback is logged-and-swallowed: progress is
/// decorative and must never abort a download.
/// </summary>
internal interface IDownloadProgress
{
    /// <summary>
    /// Fires once at the start of a download, after the HTTP response
    /// headers have arrived and before the first byte is read.
    /// </summary>
    /// <param name="totalBytes">The expected payload size from the
    /// <c>Content-Length</c> header, or <c>null</c> if the server did
    /// not declare one (e.g. chunked transfer encoding without a
    /// length).</param>
    void OnStarted(long? totalBytes);

    /// <summary>
    /// Fires roughly every 256 KB while the download is in progress.
    /// </summary>
    /// <param name="bytesReceived">Cumulative bytes read so far —
    /// monotonically non-decreasing within a single download.</param>
    void OnAdvanced(long bytesReceived);

    /// <summary>
    /// Fires exactly once after the stream copy completes, before SHA-256
    /// verification. Does not fire if the download is cancelled or
    /// throws mid-stream — the sink's <c>IDisposable</c> (if any) is
    /// the cleanup path for those cases.
    /// </summary>
    /// <param name="totalBytesReceived">Total bytes written to disk.</param>
    /// <param name="duration">Wall-clock time from the start of the
    /// HTTP request to completion of the stream copy.</param>
    void OnFinished(long totalBytesReceived, TimeSpan duration);
}

internal sealed class UpdateDownloader(HttpClient httpClient)
{
    private const int BufferSize = 64 * 1024;
    private const int ProgressEveryBytes = 256 * 1024;

    public IDownloadProgress? Progress { get; set; }

    public async Task DownloadAsync(
        Uri url,
        string? expectedSha256,
        string targetPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using HttpResponseMessage response = await httpClient.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new UpdateException(
                $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase} from {url}.");

        long? total = response.Content.Headers.ContentLength;

        using IncrementalHash? hasher = expectedSha256 is { Length: > 0 }
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;

        Stopwatch sw = Stopwatch.StartNew();
        SafeStarted(total);

        long received = 0;
        try
        {
            await using Stream input = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using FileStream output = new(
                targetPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: BufferSize, useAsync: true);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long lastReported = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hasher?.AppendData(buffer, 0, read);
                    received += read;

                    if (received - lastReported >= ProgressEveryBytes)
                    {
                        SafeAdvanced(received);
                        lastReported = received;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch
        {
            TryDelete(targetPath);
            throw;
        }

        SafeFinished(received, sw.Elapsed);

        if (hasher is null) return;

        string actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        string expected = expectedSha256!.Trim().ToLowerInvariant();

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            TryDelete(targetPath);
            throw new UpdateException(
                $"SHA-256 mismatch for {url}: expected {expected}, got {actual}.");
        }
    }

    private void SafeStarted(long? total)
    {
        if (Progress is null) return;
        try { Progress.OnStarted(total); } catch { /* progress is decorative */ }
    }

    private void SafeAdvanced(long received)
    {
        if (Progress is null) return;
        try { Progress.OnAdvanced(received); } catch { /* progress is decorative */ }
    }

    private void SafeFinished(long received, TimeSpan duration)
    {
        if (Progress is null) return;
        try { Progress.OnFinished(received, duration); } catch { /* progress is decorative */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { }
    }
}
