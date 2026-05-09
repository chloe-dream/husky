namespace Husky;

internal sealed record WatchdogOptions(
    TimeSpan IdleWindow,
    TimeSpan PingReplyTimeout,
    int MaxStrikes,
    TimeSpan TickInterval)
{
    /// <summary>
    /// Defaults from LEASH §8.2 and §8.3: probe after 10 minutes of silence,
    /// reply expected within 30 seconds, declare dead after three sequential
    /// failed probes. The tick interval is small enough that the launcher
    /// reacts quickly on the first probe and does not poll busy-waitedly.
    /// </summary>
    public static WatchdogOptions Default { get; } = new(
        IdleWindow: TimeSpan.FromMinutes(10),
        PingReplyTimeout: TimeSpan.FromSeconds(30),
        MaxStrikes: 3,
        TickInterval: TimeSpan.FromSeconds(10));
}
