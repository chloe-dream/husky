namespace Husky;

internal sealed record HuskyConfig(
    string Name,
    string Executable,
    SourceConfig Source,
    int CheckMinutes = HuskyConfig.DefaultCheckMinutes,
    int ShutdownTimeoutSec = HuskyConfig.DefaultShutdownTimeoutSec,
    int KillAfterSec = HuskyConfig.DefaultKillAfterSec,
    int RestartAttempts = HuskyConfig.DefaultRestartAttempts,
    int RestartPauseSec = HuskyConfig.DefaultRestartPauseSec)
{
    public const int DefaultCheckMinutes = 60;
    public const int MinimumCheckMinutes = 5;
    public const int DefaultShutdownTimeoutSec = 60;
    public const int DefaultKillAfterSec = 10;
    public const int DefaultRestartAttempts = 3;
    public const int DefaultRestartPauseSec = 30;
}
