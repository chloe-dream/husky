namespace Husky.Protocol;

public sealed record WelcomePayload(
    int ProtocolVersion,
    string LauncherVersion,
    bool Accepted,
    string? Reason);
