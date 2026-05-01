using Husky.Client;

namespace Husky.Client.Tests;

public sealed class HealthStatusTests
{
    [Fact]
    public void Healthy_singleton_returns_the_same_instance()
    {
        Assert.Same(HealthStatus.Healthy, HealthStatus.Healthy);
        Assert.Equal(HealthState.Healthy, HealthStatus.Healthy.State);
        Assert.Null(HealthStatus.Healthy.Details);
    }

    [Fact]
    public void With_returns_a_new_instance_and_does_not_mutate_the_original()
    {
        HealthStatus original = HealthStatus.Healthy;

        HealthStatus updated = original.With("queue", 3);

        Assert.NotSame(original, updated);
        Assert.Null(original.Details);
        Assert.NotNull(updated.Details);
        Assert.Equal(3, updated.Details!["queue"]);
    }

    [Fact]
    public void With_chained_calls_accumulate_details()
    {
        HealthStatus result = HealthStatus.Healthy
            .With("queue", 3)
            .With("guilds", 12)
            .With("queue", 5); // overrides

        Assert.NotNull(result.Details);
        Assert.Equal(2, result.Details!.Count);
        Assert.Equal(5, result.Details["queue"]);
        Assert.Equal(12, result.Details["guilds"]);
    }

    [Fact]
    public void Records_with_same_state_and_details_are_equal()
    {
        HealthStatus a = new(HealthState.Degraded, new Dictionary<string, object> { ["x"] = 1 });
        HealthStatus b = new(HealthState.Degraded, new Dictionary<string, object> { ["x"] = 1 });

        // Records compare by reference for dictionary fields, so these are NOT equal.
        // This test pins that contract — change With() carefully if you want value
        // equality on Details (would require a custom comparer).
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HealthState_enum_has_three_values_in_expected_order()
    {
        Assert.Equal(0, (int)HealthState.Healthy);
        Assert.Equal(1, (int)HealthState.Degraded);
        Assert.Equal(2, (int)HealthState.Unhealthy);
    }

    [Fact]
    public void ShutdownReason_enum_has_three_values_matching_spec()
    {
        Assert.Equal(0, (int)ShutdownReason.Update);
        Assert.Equal(1, (int)ShutdownReason.Manual);
        Assert.Equal(2, (int)ShutdownReason.LauncherStopping);
    }
}
