using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Husky;
using Husky.Protocol;

const int ExitOk = 0;
const int ExitGeneric = 1;
const int ExitConfigError = 2;

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
    return ExitConfigError;
}

string executablePath = Path.GetFullPath(Path.Combine(launcherDir, config.Executable));
if (!File.Exists(executablePath))
{
    // Bootstrap mode (LEASH §5.3.5) is wired up in step 7 once a source provider exists.
    ConsoleOutput.Husky($"executable not found: {executablePath}");
    return ExitConfigError;
}

string pipeName = PipeNaming.Generate();

await using AppPipeServer pipeServer = AppPipeServer.Create(pipeName, launcherVersion);

AppProcessOptions processOptions = new(
    ExecutablePath: executablePath,
    WorkingDirectory: Path.GetDirectoryName(executablePath)!,
    Environment: new Dictionary<string, string?>
    {
        [HuskyEnvironment.PipeNameVariable] = pipeName,
        [HuskyEnvironment.AppNameVariable] = config.Name,
    });

ConsoleOutput.Husky($"starting {config.Name}");

// Late-bound activity sink: AppProcess.Start needs the stdout/stderr
// callbacks now, but the watchdog isn't created until after the handshake.
Action? recordActivity = null;

AppProcess app;
try
{
    app = AppProcess.Start(
        processOptions,
        onStandardOutput: line => { recordActivity?.Invoke(); ConsoleOutput.AppOut(line); },
        onStandardError: line => { recordActivity?.Invoke(); ConsoleOutput.AppErr(line); });
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
{
    ConsoleOutput.Husky($"failed to start {config.Name}: {ex.Message}");
    return ExitGeneric;
}

await using (app)
{
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
        catch (ObjectDisposedException) { /* shutting down already */ }
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
        try
        {
            await pipeServer.AcceptAndHandshakeAsync(connectTimeout: TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            ConsoleOutput.Husky($"handshake failed: {ex.Message}");
            app.Kill();
            return ExitGeneric;
        }

        ConnectedApp connected = pipeServer.ConnectedApp!;
        ConsoleOutput.Husky(
            $"{connected.Name} v{connected.Version} attached (pid={connected.Pid})");

        await using Watchdog watchdog = new(pipeServer.SendPingAsync, WatchdogOptions.Default);
        pipeServer.OnActivity = watchdog.RecordActivity;
        recordActivity = watchdog.RecordActivity;
        watchdog.OnAppDeclaredDead = () =>
        {
            ConsoleOutput.Husky("no answer. growling.");
            app.Kill();
        };
        watchdog.Start();

        Task graceful = AwaitCancellationAsync(gracefulTrigger.Token);
        Task winner = await Task.WhenAny(app.ExitTask, graceful);

        if (winner == app.ExitTask)
        {
            ConsoleOutput.Husky($"{config.Name} exited with code {app.ExitCode}");
            return app.ExitCode;
        }

        ConsoleOutput.Husky("asking app to sit.");
        await StopAppGracefullyAsync(app, pipeServer, config, hardKillTrigger.Token);
        return ExitOk;
    }
    finally
    {
        Console.CancelKeyPress -= ctrlCHandler;
    }
}

static async Task StopAppGracefullyAsync(
    AppProcess app, AppPipeServer pipeServer, HuskyConfig config, CancellationToken hardKill)
{
    if (hardKill.IsCancellationRequested)
    {
        await HardKillAsync(app, "double interrupt — taking it down.");
        return;
    }

    try
    {
        await pipeServer.SendShutdownAsync(
            reason: "launcher-stopping",
            totalTimeout: TimeSpan.FromSeconds(config.ShutdownTimeoutSec),
            ackTimeout: TimeSpan.FromSeconds(5),
            ct: hardKill);
    }
    catch (TimeoutException) { ConsoleOutput.Husky("no shutdown-ack — continuing anyway."); }
    catch (IOException) { ConsoleOutput.Husky("pipe is gone — proceeding to wait."); }
    catch (OperationCanceledException) when (hardKill.IsCancellationRequested)
    {
        await HardKillAsync(app, "double interrupt — taking it down.");
        return;
    }

    if (await TryWaitForExitAsync(app, TimeSpan.FromSeconds(config.ShutdownTimeoutSec), hardKill))
    {
        ConsoleOutput.Husky("app sat down.");
        return;
    }
    if (hardKill.IsCancellationRequested)
    {
        await HardKillAsync(app, "double interrupt — taking it down.");
        return;
    }

    if (config.KillAfterSec > 0
        && await TryWaitForExitAsync(app, TimeSpan.FromSeconds(config.KillAfterSec), hardKill))
    {
        ConsoleOutput.Husky("app sat down.");
        return;
    }

    await HardKillAsync(app, "app didn't respond. growling.");
}

static async Task<bool> TryWaitForExitAsync(AppProcess app, TimeSpan timeout, CancellationToken hardKill)
{
    try
    {
        await app.ExitTask.WaitAsync(timeout, hardKill);
        return true;
    }
    catch (TimeoutException) { return false; }
    catch (OperationCanceledException) { return false; }
}

static async Task HardKillAsync(AppProcess app, string message)
{
    ConsoleOutput.Husky(message);
    app.Kill();
    try { await app.ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
    catch (TimeoutException) { /* swallow — we're tearing down */ }
    catch (OperationCanceledException) { /* normal */ }
}

static async Task AwaitCancellationAsync(CancellationToken token)
{
    TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using CancellationTokenRegistration _ = token.Register(() => tcs.TrySetResult());
    await tcs.Task.ConfigureAwait(false);
}

static string GetLauncherVersion()
{
    Assembly asm = Assembly.GetExecutingAssembly();
    string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    return string.IsNullOrWhiteSpace(info)
        ? asm.GetName().Version?.ToString() ?? "0.0.0"
        : info;
}
