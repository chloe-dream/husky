using Husky;
using Microsoft.Extensions.Time.Testing;

namespace Husky.Tests;

public sealed class RestartPolicyTests
{
    [Fact]
    public void CanRestart_is_true_when_no_attempts_recorded()
    {
        RestartPolicy policy = new(maxAttemptsPerHour: 3, pauseBetweenAttempts: TimeSpan.Zero);
        Assert.True(policy.CanRestart());
        Assert.False(policy.IsBroken);
    }

    [Fact]
    public void RecordAttempt_increments_until_cap()
    {
        FakeTimeProvider time = new();
        RestartPolicy policy = new(3, TimeSpan.Zero, time);

        policy.RecordAttempt();
        Assert.True(policy.CanRestart());
        Assert.False(policy.IsBroken);

        policy.RecordAttempt();
        Assert.True(policy.CanRestart());

        policy.RecordAttempt();
        Assert.False(policy.CanRestart());
        Assert.True(policy.IsBroken);
    }

    [Fact]
    public void Attempts_age_out_of_the_one_hour_window()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        RestartPolicy policy = new(3, TimeSpan.Zero, time);

        policy.RecordAttempt();
        policy.RecordAttempt();

        time.Advance(TimeSpan.FromMinutes(30));
        policy.RecordAttempt();
        Assert.True(policy.IsBroken); // hit cap

        time.Advance(TimeSpan.FromMinutes(31));
        // Two attempts from t=0 are now outside the rolling hour; one remains.
        Assert.Equal(1, policy.AttemptsInWindow);
        // IsBroken stays sticky until Reset.
        Assert.True(policy.IsBroken);
    }

    [Fact]
    public void Reset_clears_attempts_and_broken_flag()
    {
        FakeTimeProvider time = new();
        RestartPolicy policy = new(2, TimeSpan.Zero, time);

        policy.RecordAttempt();
        policy.RecordAttempt();
        Assert.True(policy.IsBroken);

        policy.Reset();
        Assert.False(policy.IsBroken);
        Assert.True(policy.CanRestart());
        Assert.Equal(0, policy.AttemptsInWindow);
    }

    [Fact]
    public void Pause_value_is_returned_as_supplied()
    {
        RestartPolicy policy = new(3, TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.PauseBetweenAttempts);
    }
}
