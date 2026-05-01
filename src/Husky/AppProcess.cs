using System.Diagnostics;

namespace Husky;

internal sealed class AppProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly TaskCompletionSource exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int exitCode;
    private volatile bool hasExited;
    private bool disposed;

    public int ProcessId { get; }
    public bool HasExited => hasExited;
    public int ExitCode => exitCode;
    public Task ExitTask => exitTcs.Task;

    private AppProcess(Process process)
    {
        this.process = process;
        ProcessId = process.Id;

        process.Exited += OnExited;

        // Subscription races with process termination — re-check after subscribing.
        if (process.HasExited) OnExited(this, EventArgs.Empty);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (hasExited) return;
        try { exitCode = process.ExitCode; }
        catch (InvalidOperationException) { exitCode = -1; }
        hasExited = true;
        exitTcs.TrySetResult();
    }

    public static AppProcess Start(
        AppProcessOptions options,
        Action<string>? onStandardOutput = null,
        Action<string>? onStandardError = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(options.ExecutablePath))
            throw new FileNotFoundException(
                $"Executable not found: '{options.ExecutablePath}'.", options.ExecutablePath);

        ProcessStartInfo psi = new()
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (options.Environment is { } env)
        {
            foreach (KeyValuePair<string, string?> entry in env)
            {
                psi.Environment[entry.Key] = entry.Value;
            }
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

        try
        {
            if (!proc.Start())
                throw new InvalidOperationException(
                    $"Failed to start '{options.ExecutablePath}'.");
        }
        catch
        {
            proc.Dispose();
            throw;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        return new AppProcess(proc);
    }

    public void Kill()
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* already exited or already disposed */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        Kill();

        try
        {
            await ExitTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException) { /* exit task did not complete — proceed with cleanup */ }
        catch (OperationCanceledException) { /* normal */ }

        process.Dispose();
    }
}
