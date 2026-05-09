namespace Husky;

/// <summary>
/// The shape of <c>husky.config.json</c> as the user authors it locally.
/// Every field is optional: <see cref="Source"/> may be null when CLI
/// flags supply one (LEASH §5.2.1), and the rest may come from
/// source-supplied config (LEASH §5.2). Pass through
/// <see cref="HuskyConfigResolver"/> to obtain the effective
/// <see cref="HuskyConfig"/>.
/// </summary>
internal sealed record LocalHuskyConfig(
    SourceConfig? Source = null,
    string? Name = null,
    string? Executable = null,
    int? CheckMinutes = null,
    int? ShutdownTimeoutSec = null,
    int? KillAfterSec = null,
    int? RestartAttempts = null,
    int? RestartPauseSec = null);
