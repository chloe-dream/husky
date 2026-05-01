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
    public const string ShutdownProgress = "shutdown-progress";
}
