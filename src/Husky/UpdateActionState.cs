namespace Husky;

/// <summary>
/// TUI action-bar visibility for the <c>[u]</c> update-now command
/// (LEASH §10.4). The launcher derives this from the connected app's
/// capabilities and the cached <see cref="UpdateInfo"/> state, then
/// pushes it into the chrome via
/// <see cref="ConsoleOutput.SetUpdateActionState(UpdateActionState)"/>.
/// </summary>
internal enum UpdateActionState
{
    /// <summary>
    /// No app attached, or the connected app does not advertise the
    /// <c>manual-updates</c> capability. The hint is omitted from the
    /// action bar entirely — the spec says <c>[u]</c> is hidden in
    /// this case because there is no app-side trigger to mirror.
    /// </summary>
    Hidden,

    /// <summary>
    /// App supports manual updates but no fresh <see cref="UpdateInfo"/>
    /// is cached. The hint renders in a dimmed colour and the hotkey
    /// still fires, but the launcher only logs that nothing is cached.
    /// </summary>
    Disabled,

    /// <summary>
    /// App supports manual updates and an update is cached and ready
    /// to apply. The hint renders in the launcher's accent colour
    /// like the other action-bar entries.
    /// </summary>
    Enabled,
}
