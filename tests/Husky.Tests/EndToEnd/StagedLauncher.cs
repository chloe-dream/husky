using System.Diagnostics;

namespace Husky.Tests.EndToEnd;

internal sealed class StagedLauncher : IAsyncDisposable
{
    public string RootDirectory { get; }
    public string LauncherPath { get; }
    public string ConfigPath { get; }
    public string AppDirectory { get; }
    public string AppRelativeExecutable { get; }
    public string AppExecutablePath => Path.Combine(AppDirectory, Path.GetFileName(AppRelativeExecutable));

    private readonly List<Process> processes = new();

    private StagedLauncher(
        string root,
        string launcher,
        string config,
        string appDir,
        string appRelative)
    {
        RootDirectory = root;
        LauncherPath = launcher;
        ConfigPath = config;
        AppDirectory = appDir;
        AppRelativeExecutable = appRelative;
    }

    public static StagedLauncher Create()
    {
        string root = Path.Combine(Path.GetTempPath(), $"husky-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        CopyDirectory(LauncherLocator.ResolveDirectory(), root);

        string appDir = Path.Combine(root, "app");
        Directory.CreateDirectory(appDir);
        CopyDirectory(TestAppLocator.ResolveDirectory(), appDir);

        string launcherFile = Path.Combine(root, OperatingSystem.IsWindows() ? "Husky.exe" : "Husky");
        if (!OperatingSystem.IsWindows()) MakeExecutable(launcherFile);

        string appExeFile = OperatingSystem.IsWindows() ? "Husky.TestApp.exe" : "Husky.TestApp";
        if (!OperatingSystem.IsWindows()) MakeExecutable(Path.Combine(appDir, appExeFile));

        string configPath = Path.Combine(root, "husky.config.json");
        string appRelative = $"app/{appExeFile}";

        return new StagedLauncher(root, launcherFile, configPath, appDir, appRelative);
    }

    public void WriteDefaultConfig(string name = "smoke-app")
    {
        WriteConfig($$"""
            {
              "name": "{{name}}",
              "executable": "{{AppRelativeExecutable}}",
              "source": {
                "type": "github",
                "repo": "x/y",
                "asset": "y-{version}.zip"
              }
            }
            """);
    }

    public void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

    public Process Start(
        Action<string>? onStandardOutput = null,
        Action<string>? onStandardError = null,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        ProcessStartInfo psi = new()
        {
            FileName = LauncherPath,
            WorkingDirectory = RootDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (extraEnv is not null)
        {
            foreach (KeyValuePair<string, string?> kvp in extraEnv)
                psi.Environment[kvp.Key] = kvp.Value;
        }

        Process proc = new() { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onStandardOutput?.Invoke(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onStandardError?.Invoke(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        processes.Add(proc);
        return proc;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Process proc in processes)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            try { await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }

            proc.Dispose();
        }

        try { Directory.Delete(RootDirectory, recursive: true); }
        catch (IOException) { /* swallow — Windows may still hold a handle briefly */ }
        catch (UnauthorizedAccessException) { }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), overwrite: true);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch (PlatformNotSupportedException) { }
    }
}
