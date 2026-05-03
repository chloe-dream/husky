using Husky.Protocol;

namespace Husky;

internal sealed record ConnectedApp(
    string Name,
    string Version,
    int Pid,
    IReadOnlyList<string> Capabilities,
    string UpdateMode)
{
    public bool SupportsManualUpdates => Capabilities.Contains(Protocol.Capabilities.ManualUpdates);
}
