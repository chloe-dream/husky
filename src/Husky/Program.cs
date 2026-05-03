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

// Step 1 — load the local config. Only `source` is required at this point;
// `name` and `executable` may come from source-supplied config (LEASH §5.2).
LocalHuskyConfig localConfig;
try
{
    localConfig = HuskyConfigLoader.Load(configPath);
}
catch (HuskyConfigException ex)
{
    ConsoleOutput.Husky($"config error: {ex.Message}");
    return ExitCodes.ConfigError;
}

using HttpClient httpClient = BuildHttpClient(launcherVersion);

// Step 2 — initialise the source provider.
IUpdateSource source;
try
{
    source = UpdateSourceFactory.Create(localConfig.Source, httpClient, launcherVersion);
}
catch (HuskyConfigException ex)
{
    ConsoleOutput.Husky($"config error: {ex.Message}");
    return ExitCodes.ConfigError;
}

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

// Step 3 — initial source poll. Currentversion "0.0.0" makes the providers
// always return the latest available release (so we get the source-supplied
// config block whether or not we'd update). A network failure here is OK if
// the local config can stand on its own.
ConsoleOutput.Husky("sniffing for updates...");
UpdateInfo? bootPoll = null;
try
{
    bootPoll = await source.CheckForUpdateAsync("0.0.0", gracefulTrigger.Token).ConfigureAwait(false);
    if (bootPoll?.SourceFieldDropped == true)
    {
        ConsoleOutput.Husky(
            "source-supplied config contained a 'source' block — dropped (anti-redirect, LEASH §9.2).");
    }
}
catch (OperationCanceledException) when (gracefulTrigger.IsCancellationRequested)
{
    return ExitCodes.Ok;
}
catch (Exception ex)
{
    ConsoleOutput.Husky($"initial source poll failed: {ex.Message}");
}

// Step 4 — resolve the effective config (local + source-supplied + defaults).
HuskyConfig config;
try
{
    config = HuskyConfigResolver.Resolve(localConfig, bootPoll?.Config);
}
catch (HuskyConfigException ex)
{
    ConsoleOutput.Husky(bootPoll is null
        ? $"config incomplete and source unreachable: {ex.Message}"
        : $"config error: {ex.Message}");
    return ExitCodes.ConfigError;
}

string executablePath = Path.GetFullPath(Path.Combine(launcherDir, config.Executable));
string installDirectory = launcherDir;

UpdateDownloader downloader = new(httpClient);
long lastReportedMb = -1;
downloader.OnProgress = (received, total) =>
{
    long mb = received / (1024 * 1024);
    if (mb == lastReportedMb && total is not null && received < total) return;
    lastReportedMb = mb;
    string totalText = total is { } t ? $" / {HumanBytes.Format(t)}" : "";
    ConsoleOutput.Husky($"fetching... {HumanBytes.Format(received)}{totalText}");
};
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
    executablePath: executablePath,
    seedUpdateInfo: bootPoll);

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
