namespace Husky.Tests.Fixtures;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    private TempDirectory(string path) => Path = path;

    public static TempDirectory Create(string? prefix = null)
    {
        string baseName = string.IsNullOrEmpty(prefix) ? "husky-test" : prefix;
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{baseName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { /* swallow — Windows may briefly hold a handle */ }
        catch (UnauthorizedAccessException) { }
    }
}
