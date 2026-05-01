namespace Husky.Client;

public sealed record HealthStatus(
    HealthState State,
    IReadOnlyDictionary<string, object>? Details = null)
{
    public static HealthStatus Healthy { get; } = new(HealthState.Healthy);

    public HealthStatus With(string key, object value)
    {
        Dictionary<string, object> next = Details is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(Details);
        next[key] = value;
        return this with { Details = next };
    }
}
