namespace Husky;

internal sealed class Watchdog : IAsyncDisposable
{
    private readonly Func<TimeSpan, CancellationToken, Task<bool>> sendPing;
    private readonly WatchdogOptions options;
    private readonly CancellationTokenSource lifetimeCts = new();
    private readonly TimeProvider clock;

    private long lastActivityTicks;
    private int strikes;
    private Task? loop;
    private bool disposed;

    public Action? OnAppDeclaredDead { get; set; }

    internal Task? Loop => loop;
    internal int Strikes => strikes;

    public Watchdog(
        Func<TimeSpan, CancellationToken, Task<bool>> sendPing,
        WatchdogOptions options,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(sendPing);
        ArgumentNullException.ThrowIfNull(options);

        this.sendPing = sendPing;
        this.options = options;
        this.clock = clock ?? TimeProvider.System;
        lastActivityTicks = this.clock.GetUtcNow().UtcTicks;
    }

    public void RecordActivity() =>
        Interlocked.Exchange(ref lastActivityTicks, clock.GetUtcNow().UtcTicks);

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (loop is not null)
            throw new InvalidOperationException("Watchdog has already been started.");

        RecordActivity();
        loop = Task.Run(() => RunAsync(lifetimeCts.Token), CancellationToken.None);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(options.TickInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                TimeSpan idle = clock.GetUtcNow() -
                    new DateTimeOffset(Interlocked.Read(ref lastActivityTicks), TimeSpan.Zero);
                if (idle < options.IdleWindow) continue;

                bool acked;
                try
                {
                    acked = await sendPing(options.PingReplyTimeout, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // Pipe write failed — treat as a missed probe.
                    acked = false;
                }

                if (acked)
                {
                    Interlocked.Exchange(ref strikes, 0);
                    continue;
                }

                int next = Interlocked.Increment(ref strikes);
                if (next >= options.MaxStrikes)
                {
                    OnAppDeclaredDead?.Invoke();
                    return;
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try { lifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }

        if (loop is not null)
        {
            try { await loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { }
        }

        lifetimeCts.Dispose();
    }
}
