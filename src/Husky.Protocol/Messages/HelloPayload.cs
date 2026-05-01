namespace Husky.Protocol;

public sealed record HelloPayload(
    int ProtocolVersion,
    string AppVersion,
    string AppName,
    int Pid);
