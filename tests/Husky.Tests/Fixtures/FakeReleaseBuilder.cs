using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Husky.Tests.Fixtures;

/// <summary>
/// Builds ZIP packages for update-flow tests. Entries are added with
/// <see cref="WithFile(string, byte[])"/>; <see cref="BuildZip"/> writes the
/// archive to disk and returns the path. The executable inside the package is
/// a few placeholder bytes by default — tests for the extractor's sanity
/// check care about the path, not the binary's executability.
/// </summary>
internal sealed class FakeReleaseBuilder
{
    private readonly Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

    public FakeReleaseBuilder WithFile(string relativePath, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        entries[relativePath.Replace('\\', '/')] = content;
        return this;
    }

    public FakeReleaseBuilder WithFile(string relativePath, string text) =>
        WithFile(relativePath, Encoding.UTF8.GetBytes(text));

    public FakeReleaseBuilder WithExecutable(string relativePath, string body = "fake-executable") =>
        WithFile(relativePath, body);

    public string BuildZip(string targetDirectory, string fileName)
    {
        Directory.CreateDirectory(targetDirectory);
        string zipPath = Path.Combine(targetDirectory, fileName);

        using FileStream fs = File.Create(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Create, leaveOpen: false);
        foreach (KeyValuePair<string, byte[]> entry in entries)
        {
            ZipArchiveEntry zipEntry = archive.CreateEntry(entry.Key);
            using Stream entryStream = zipEntry.Open();
            entryStream.Write(entry.Value);
        }

        return zipPath;
    }

    public static string Sha256(string path)
    {
        using FileStream fs = File.OpenRead(path);
        byte[] hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void TruncateInPlace(string path, double fraction = 0.5)
    {
        long size = new FileInfo(path).Length;
        using FileStream fs = File.Open(path, FileMode.Open, FileAccess.Write);
        fs.SetLength((long)(size * fraction));
    }
}
