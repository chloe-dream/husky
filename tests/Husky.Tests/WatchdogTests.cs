using Husky;

namespace Husky.Tests;

public sealed class WatchdogTests
{
    private static readonly WatchdogOptions FastOptions = new(
        IdleWindow: TimeSpan.FromMilliseconds(120),
        PingReplyTimeout: TimeSpan.FromMilliseconds(80),
        MaxStrikes: 3,
        TickInterval: TimeSpan.FromMilliseconds(40));

    [Fact]
    public async Task Watchdog_does_not_ping_when_activity_keeps_arriving()
    {
        Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new();
        int pingCount = 0;
        await using Watchdog watchdog = new(
            sendPing: (_, _) =>
            {
                Interlocked.Increment(ref pingCount);
                return Task.FromResult(true);
            },
            options: FastOptions,
            clock: time);

        watchdog.Start();

        // Advance time in slices smaller than IdleWindow, recording activity
        // before each slice — the loop must never see idle > IdleWindow.
        for (int i = 0; i < 10; i++)
        {
            watchdog.RecordActivity();
            time.Advance(TimeSpan.FromMilliseconds(80));
            await Task.Yield(); // let the loop wake from its TimeProvider delay
        }

        Assert.Equal(0, Volatile.Read(ref pingCount));
    }

    [Fact]
    public async Task Watchdog_sends_a_ping_after_the_idle_window()
    {
        TaskCompletionSource pingSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Watchdog watchdog = new(
            sendPing: (_, _) =>
            {
                pingSeen.TrySetResult();
                return Task.FromResult(true);
            },
            options: FastOptions);

        watchdog.Start();

        await pingSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Watchdog_resets_strikes_when_a_subsequent_ping_succeeds()
    {
        int call = 0;
        TaskCompletionSource thirdCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool deadFired = false;

        await using Watchdog watchdog = new(
            sendPing: (_, _) =>
            {
                int n = Interlocked.Increment(ref call);
                bool result = n == 1
                    ? false   // strike 1
                    : n == 2 ? true  // strike reset
                    : true;          // any further calls keep succeeding
                if (n == 3) thirdCall.TrySetResult();
                return Task.FromResult(result);
            },
            options: FastOptions);

        watchdog.OnAppDeclaredDead = () => deadFired = true;
        watchdog.Start();

        await thirdCall.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(deadFired);
        Assert.Equal(0, watchdog.Strikes);
    }

    [Fact]
    public async Task Watchdog_declares_app_dead_after_max_strikes_in_a_row()
    {
        TaskCompletionSource dead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Watchdog watchdog = new(
            sendPing: (_, _) => Task.FromResult(false),
            options: FastOptions);

        watchdog.OnAppDeclaredDead = () => dead.TrySetResult();
        watchdog.Start();

        await dead.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(FastOptions.MaxStrikes, watchdog.Strikes);
    }

    [Fact]
    public async Task Watchdog_treats_a_throwing_pinger_as_a_missed_probe()
    {
        TaskCompletionSource dead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using Watchdog watchdog = new(
            sendPing: (_, _) => throw new IOException("pipe gone"),
            options: FastOptions);

        watchdog.OnAppDeclaredDead = () => dead.TrySetResult();
        watchdog.Start();

        await dead.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Watchdog_after_OnAppDeclaredDead_does_not_ping_again()
    {
        int callsBeforeDead = 0;
        int callsTotal = 0;
        TaskCompletionSource dead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using Watchdog watchdog = new(
            sendPing: (_, _) =>
            {
                Interlocked.Increment(ref callsTotal);
                return Task.FromResult(false);
            },
            options: FastOptions);

        watchdog.OnAppDeclaredDead = () =>
        {
            callsBeforeDead = Volatile.Read(ref callsTotal);
            dead.TrySetResult();
        };

        watchdog.Start();
        await dead.Task.WaitAsync(TimeSpan.FromSeconds(3));

        // Give the loop a moment to spin if it (incorrectly) keeps running.
        await Task.Delay(200);
        Assert.Equal(callsBeforeDead, Volatile.Read(ref callsTotal));
    }

    [Fact]
    public async Task Start_throws_when_called_twice()
    {
        await using Watchdog watchdog = new(
            sendPing: (_, _) => Task.FromResult(true),
            options: FastOptions);

        watchdog.Start();
        Assert.Throws<InvalidOperationException>(watchdog.Start);
    }

    [Fact]
    public async Task DisposeAsync_stops_the_loop_cleanly()
    {
        Watchdog watchdog = new(
            sendPing: (_, _) => Task.FromResult(true),
            options: FastOptions);

        watchdog.Start();
        await watchdog.DisposeAsync();

        Assert.NotNull(watchdog.Loop);
        Assert.True(watchdog.Loop!.IsCompleted);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        Watchdog watchdog = new(
            sendPing: (_, _) => Task.FromResult(true),
            options: FastOptions);

        await watchdog.DisposeAsync();
        await watchdog.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task Start_after_Dispose_throws()
    {
        Watchdog watchdog = new(
            sendPing: (_, _) => Task.FromResult(true),
            options: FastOptions);

        await watchdog.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(watchdog.Start);
    }
}
