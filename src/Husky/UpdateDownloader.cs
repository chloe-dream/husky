using System.Buffers;
using System.Security.Cryptography;

namespace Husky;

internal sealed class UpdateDownloader(HttpClient httpClient)
{
    private const int BufferSize = 64 * 1024;

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

        using IncrementalHash? hasher = expectedSha256 is { Length: > 0 }
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;

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
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hasher?.AppendData(buffer, 0, read);
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { }
    }
}
