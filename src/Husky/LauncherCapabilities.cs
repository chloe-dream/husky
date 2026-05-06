using Husky.Protocol;

namespace Husky;

internal static class LauncherCapabilities
{
    /// <summary>
    /// The capability tokens the launcher always advertises in
    /// <c>welcome.capabilities</c>. The launcher implements both wire features
    /// regardless of which apps connect; the *intersection* with each app's
    /// declared capabilities determines what messages flow on a given session.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Capabilities.ManualUpdates,
    };
}
