namespace Husky.Protocol;

public sealed record ShutdownPayload(
    string Reason,
    int TimeoutSeconds);
