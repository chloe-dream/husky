namespace Husky.Client;

public sealed record HuskyClientOptions
{
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan WelcomeTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestReplyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public HuskyUpdateMode UpdateMode { get; set; } = HuskyUpdateMode.Auto;

    public static HuskyClientOptions Default => new();
}
