using Husky;

namespace Husky.Tests;

public sealed class UpdateSchedulerTests
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(60);

    [Fact]
    public async Task Tick_fires_after_each_interval()
    {
        int ticks = 0;
        TaskCompletionSource thirdTick = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using UpdateScheduler scheduler = new(
            interval: FastInterval,
            tickAsync: _ =>
            {
                if (Interlocked.Increment(ref ticks) >= 3) thirdTick.TrySetResult();
                return Task.CompletedTask;
            });
        scheduler.Start();

        await thirdTick.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref ticks) >= 3);
    }

    [Fact]
    public async Task Exceptions_in_tick_do_not_stop_the_loop()
    {
        int call = 0;
        TaskCompletionSource secondTick = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using UpdateScheduler scheduler = new(
            interval: FastInterval,
            tickAsync: _ =>
            {
                int n = Interlocked.Increment(ref call);
                if (n == 1) throw new InvalidOperationException("first tick blew up");
                if (n == 2) secondTick.TrySetResult();
                return Task.CompletedTask;
            });
        scheduler.Start();

        await secondTick.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispose_stops_the_loop_and_cancels_a_running_tick()
    {
        TaskCompletionSource tickStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource tickCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        UpdateScheduler scheduler = new(
            interval: FastInterval,
            tickAsync: async ct =>
            {
                tickStarted.TrySetResult();
                try { await Task.Delay(TimeSpan.FromMinutes(10), ct); }
                catch (OperationCanceledException) { tickCancelled.TrySetResult(); throw; }
            });
        scheduler.Start();

        await tickStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.DisposeAsync();
        await tickCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Start_is_idempotent()
    {
        int ticks = 0;
        TaskCompletionSource firstTick = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using UpdateScheduler scheduler = new(
            interval: TimeSpan.FromMilliseconds(200),
            tickAsync: _ =>
            {
                Interlocked.Increment(ref ticks);
                firstTick.TrySetResult();
                return Task.CompletedTask;
            });

        scheduler.Start();
        scheduler.Start();
        scheduler.Start();

        await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // One interval = one tick, no matter how many Start calls.
        Assert.Equal(1, Volatile.Read(ref ticks));
    }
}
