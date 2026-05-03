namespace Husky;

/// <summary>
/// Merges a <see cref="LocalHuskyConfig"/> (parsed from disk) with an
/// optional <see cref="SourceSuppliedConfig"/> (pulled from the source) into
/// the effective <see cref="HuskyConfig"/> the launcher runs against. See
/// LEASH §5.2 for the precedence rules: local wins over source-supplied
/// wins over built-in defaults.
/// </summary>
internal static class HuskyConfigResolver
{
    public static HuskyConfig Resolve(
        LocalHuskyConfig local,
        SourceSuppliedConfig? supplied)
    {
        ArgumentNullException.ThrowIfNull(local);

        string? name = First(local.Name, supplied?.Name);
        string? executable = First(local.Executable, supplied?.Executable);

        if (string.IsNullOrWhiteSpace(name))
            throw new HuskyConfigException(
                "Config field 'name' is missing — set it locally or in source-supplied config.");

        if (string.IsNullOrWhiteSpace(executable))
            throw new HuskyConfigException(
                "Config field 'executable' is missing — set it locally or in source-supplied config.");

        ValidateExecutablePath(executable);

        int checkMinutes = local.CheckMinutes ?? supplied?.CheckMinutes ?? HuskyConfig.DefaultCheckMinutes;
        if (checkMinutes < HuskyConfig.MinimumCheckMinutes)
            throw new HuskyConfigException(
                $"Config field 'checkMinutes' must be at least {HuskyConfig.MinimumCheckMinutes}; got {checkMinutes}.");

        int shutdownTimeoutSec = local.ShutdownTimeoutSec ?? supplied?.ShutdownTimeoutSec ?? HuskyConfig.DefaultShutdownTimeoutSec;
        if (shutdownTimeoutSec <= 0)
            throw new HuskyConfigException(
                $"Config field 'shutdownTimeoutSec' must be positive; got {shutdownTimeoutSec}.");

        int killAfterSec = local.KillAfterSec ?? supplied?.KillAfterSec ?? HuskyConfig.DefaultKillAfterSec;
        if (killAfterSec < 0)
            throw new HuskyConfigException(
                $"Config field 'killAfterSec' must be non-negative; got {killAfterSec}.");

        int restartAttempts = local.RestartAttempts ?? supplied?.RestartAttempts ?? HuskyConfig.DefaultRestartAttempts;
        if (restartAttempts < 0)
            throw new HuskyConfigException(
                $"Config field 'restartAttempts' must be non-negative; got {restartAttempts}.");

        int restartPauseSec = local.RestartPauseSec ?? supplied?.RestartPauseSec ?? HuskyConfig.DefaultRestartPauseSec;
        if (restartPauseSec < 0)
            throw new HuskyConfigException(
                $"Config field 'restartPauseSec' must be non-negative; got {restartPauseSec}.");

        return new HuskyConfig(
            Name: name!,
            Executable: NormalizeExecutable(executable!),
            Source: local.Source,
            CheckMinutes: checkMinutes,
            ShutdownTimeoutSec: shutdownTimeoutSec,
            KillAfterSec: killAfterSec,
            RestartAttempts: restartAttempts,
            RestartPauseSec: restartPauseSec);
    }

    private static string? First(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : null);

    private static void ValidateExecutablePath(string executable)
    {
        if (Path.IsPathRooted(executable))
            throw new HuskyConfigException(
                $"Config field 'executable' must be relative to the launcher directory, not absolute: '{executable}'.");

        string normalized = executable.Replace('\\', '/');
        foreach (string segment in normalized.Split('/', StringSplitOptions.None))
        {
            if (segment == "..")
                throw new HuskyConfigException(
                    $"Config field 'executable' may not contain '..' segments: '{executable}'.");
        }
    }

    private static string NormalizeExecutable(string executable) =>
        executable.Replace('\\', '/');
}
