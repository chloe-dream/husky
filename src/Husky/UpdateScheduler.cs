namespace Husky;

/// <summary>
/// Periodic update poller (LEASH §5.3.7). Runs an async tick on the
/// configured interval and serializes ticks (no concurrent invocations).
/// Stop via <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class UpdateScheduler(
    TimeSpan interval,
    Func<CancellationToken, Task> tickAsync) : IAsyncDisposable
{
    private readonly CancellationTokenSource cts = new();
    private Task? loop;
    private bool disposed;

    public TimeSpan Interval { get; } = interval;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (loop is not null) return;
        loop = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;

            try
            {
                await tickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                // Tick errors are swallowed at this layer — the tick callback
                // surfaces them via console output. The scheduler keeps
                // ticking on the configured cadence.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try { await cts.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }

        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        cts.Dispose();
    }
}
