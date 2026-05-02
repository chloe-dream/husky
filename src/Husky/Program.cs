using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Husky;

// The banner art uses Unicode block characters; force UTF-8 so Windows
// consoles on legacy code pages render them instead of '?'.
Console.OutputEncoding = Encoding.UTF8;

string launcherDir = AppContext.BaseDirectory;
string configPath = Path.Combine(launcherDir, HuskyConfigLoader.DefaultFileName);

string launcherVersion = GetLauncherVersion();
Banner.Render(launcherVersion);

HuskyConfig config;
try
{
    config = HuskyConfigLoader.Load(configPath);
}
catch (HuskyConfigException ex)
{
    ConsoleOutput.Husky($"config error: {ex.Message}");
    return ExitCodes.ConfigError;
}

string executablePath = Path.GetFullPath(Path.Combine(launcherDir, config.Executable));
string installDirectory = launcherDir;

using HttpClient httpClient = BuildHttpClient(launcherVersion);

IUpdateSource source;
try
{
    source = UpdateSourceFactory.Create(config.Source, httpClient, launcherVersion);
}
catch (HuskyConfigException ex)
{
    ConsoleOutput.Husky($"config error: {ex.Message}");
    return ExitCodes.ConfigError;
}

UpdateDownloader downloader = new(httpClient);
using UpdateFlow updateFlow = new(downloader, installDirectory, config.Executable);

AppSessionLauncher sessionLauncher = new(
    executablePath: executablePath,
    appName: config.Name,
    launcherVersion: launcherVersion,
    watchdogOptions: WatchdogOptions.Default,
    onStandardOutput: ConsoleOutput.AppOut,
    onStandardError: ConsoleOutput.AppErr);

RestartPolicy restartPolicy = new(
    maxAttemptsPerHour: config.RestartAttempts,
    pauseBetweenAttempts: TimeSpan.FromSeconds(config.RestartPauseSec));

LauncherRuntime runtime = new(
    config: config,
    source: source,
    updateFlow: updateFlow,
    sessionLauncher: sessionLauncher,
    restartPolicy: restartPolicy,
    executablePath: executablePath);

int sigintCount = 0;
using CancellationTokenSource gracefulTrigger = new();
using CancellationTokenSource hardKillTrigger = new();

void OnInterrupt()
{
    int n = Interlocked.Increment(ref sigintCount);
    try
    {
        if (n == 1) gracefulTrigger.Cancel();
        else hardKillTrigger.Cancel();
    }
    catch (ObjectDisposedException) { /* shutting down */ }
}

ConsoleCancelEventHandler ctrlCHandler = (_, args) =>
{
    args.Cancel = true;
    OnInterrupt();
};
Console.CancelKeyPress += ctrlCHandler;

using PosixSignalRegistration sigtermReg = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    ctx =>
    {
        ctx.Cancel = true;
        OnInterrupt();
    });

try
{
    return await runtime.RunAsync(gracefulTrigger.Token, hardKillTrigger.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= ctrlCHandler;
}

static HttpClient BuildHttpClient(string launcherVersion)
{
    HttpClient client = new() { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"Husky/{launcherVersion}");
    return client;
}

static string GetLauncherVersion()
{
    Assembly asm = Assembly.GetExecutingAssembly();
    string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    return string.IsNullOrWhiteSpace(info)
        ? asm.GetName().Version?.ToString() ?? "0.0.0"
        : info;
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Generic = 1;
    public const int ConfigError = 2;
}
