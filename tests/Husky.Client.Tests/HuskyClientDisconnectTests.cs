using Husky.Client;
using Husky.Protocol;

namespace Husky.Client.Tests;

public sealed class HuskyClientDisconnectTests
{
    [Fact]
    public async Task Pipe_close_fires_ShutdownToken_with_LauncherStopping()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        TaskCompletionSource<ShutdownReason> reasonTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Client.OnShutdown((reason, _) =>
        {
            reasonTcs.TrySetResult(reason);
            return Task.CompletedTask;
        });

        h.Harness.Server.Disconnect();

        ShutdownReason reason = await reasonTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ShutdownReason.LauncherStopping, reason);

        await WaitForCancellationAsync(TimeSpan.FromSeconds(2), h.Client.ShutdownToken);
    }

    [Fact]
    public async Task Pipe_close_without_handler_still_fires_ShutdownToken()
    {
        await using ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        h.Harness.Server.Disconnect();

        await WaitForCancellationAsync(TimeSpan.FromSeconds(3), h.Client.ShutdownToken);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        await h.DisposeAsync();
        await h.DisposeAsync(); // must not throw
    }

    [Fact]
    public async Task DisposeAsync_completes_within_a_few_seconds_even_with_loops_running()
    {
        ConnectedHandshake h = await ConnectedHandshake.PerformAsync();

        TimeSpan elapsed = await MeasureAsync(() => h.DisposeAsync().AsTask());

        Assert.True(
            elapsed < TimeSpan.FromSeconds(5),
            $"DisposeAsync took {elapsed.TotalMilliseconds:F0} ms — loops did not drain.");
    }

    private static async Task<TimeSpan> MeasureAsync(Func<Task> action)
    {
        long start = Environment.TickCount64;
        await action();
        return TimeSpan.FromMilliseconds(Environment.TickCount64 - start);
    }

    private static async Task WaitForCancellationAsync(TimeSpan timeout, CancellationToken token)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration _ = token.Register(() => tcs.TrySetResult());
        Task winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout, CancellationToken.None));
        if (winner != tcs.Task)
            throw new TimeoutException($"CancellationToken did not fire within {timeout}.");
    }
}
