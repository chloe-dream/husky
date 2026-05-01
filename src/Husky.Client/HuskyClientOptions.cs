namespace Husky.Client;

internal sealed record HuskyClientOptions(
    TimeSpan ConnectTimeout,
    TimeSpan WelcomeTimeout,
    TimeSpan HeartbeatInterval)
{
    public static HuskyClientOptions Default { get; } = new(
        ConnectTimeout: TimeSpan.FromSeconds(5),
        WelcomeTimeout: TimeSpan.FromSeconds(5),
        HeartbeatInterval: TimeSpan.FromSeconds(5));
}
