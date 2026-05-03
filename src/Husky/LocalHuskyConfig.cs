namespace Husky;

/// <summary>
/// The shape of <c>husky.config.json</c> as the user authors it locally.
/// Only <see cref="Source"/> is required; everything else may be omitted
/// (and supplied by the source itself, see LEASH §5.2). Pass through
/// <see cref="HuskyConfigResolver"/> to obtain the effective
/// <see cref="HuskyConfig"/>.
/// </summary>
internal sealed record LocalHuskyConfig(
    SourceConfig Source,
    string? Name = null,
    string? Executable = null,
    int? CheckMinutes = null,
    int? ShutdownTimeoutSec = null,
    int? KillAfterSec = null,
    int? RestartAttempts = null,
    int? RestartPauseSec = null);
