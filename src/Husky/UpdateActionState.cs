namespace Husky;

/// <summary>
/// TUI action-bar visibility for the <c>[u]</c> check-and-install command
/// (LEASH §10.4). The launcher derives this purely from the connected
/// app's capabilities — when shown, the hotkey is always actionable and
/// fires a fresh source poll (followed by an apply if anything newer is
/// found). Pushed at the chrome via
/// <see cref="ConsoleOutput.SetUpdateActionState(UpdateActionState)"/>.
/// </summary>
internal enum UpdateActionState
{
    /// <summary>
    /// No app attached, or the connected app does not advertise the
    /// <c>manual-updates</c> capability. The hint is omitted from the
    /// action bar entirely.
    /// </summary>
    Hidden,

    /// <summary>
    /// App supports manual updates. The hint renders in the launcher's
    /// accent colour and the hotkey forces a fresh source poll on press —
    /// no longer gated by whether a cached <see cref="UpdateInfo"/> exists.
    /// </summary>
    Enabled,
}
