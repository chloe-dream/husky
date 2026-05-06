namespace Husky.Protocol;

public static class MessageTypes
{
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Heartbeat = "heartbeat";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Shutdown = "shutdown";
    public const string ShutdownAck = "shutdown-ack";
    public const string UpdateCheck = "update-check";
    public const string UpdateStatus = "update-status";
    public const string UpdateAvailable = "update-available";
    public const string UpdateNow = "update-now";
    public const string SetUpdateMode = "set-update-mode";
    public const string UpdateModeAck = "update-mode-ack";
}
