namespace Husky;

/// <summary>
/// Deployment metadata an app author may supply alongside their release —
/// fetched from the configured source (GitHub asset/repo file or HTTP
/// manifest). Every field is optional; the launcher merges it with the
/// local config per LEASH §5.2 precedence rules.
/// </summary>
internal sealed record SourceSuppliedConfig(
    string? Name = null,
    string? Executable = null,
    int? CheckMinutes = null,
    int? ShutdownTimeoutSec = null,
    int? KillAfterSec = null,
    int? RestartAttempts = null,
    int? RestartPauseSec = null);
