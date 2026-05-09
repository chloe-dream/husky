using System.Text.Json;
using System.Text.Json.Serialization;

namespace Husky;

/// <summary>
/// Wire shape for a source-supplied config block (HTTP manifest's
/// <c>config:</c> field, or the contents of a <c>husky.config.json</c>
/// pulled from a GitHub release asset / repo root file). Every field is
/// optional. The <c>source</c> field is parsed as a raw <see cref="JsonElement"/>
/// purely so the launcher can detect — and warn about — an app author who
/// accidentally tried to redirect users via source-supplied config (LEASH
/// §9.2 / §9.3 anti-redirect rule).
/// </summary>
internal sealed record SourceSuppliedConfigDto(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("executable")] string? Executable = null,
    [property: JsonPropertyName("checkMinutes")] int? CheckMinutes = null,
    [property: JsonPropertyName("shutdownTimeoutSec")] int? ShutdownTimeoutSec = null,
    [property: JsonPropertyName("killAfterSec")] int? KillAfterSec = null,
    [property: JsonPropertyName("restartAttempts")] int? RestartAttempts = null,
    [property: JsonPropertyName("restartPauseSec")] int? RestartPauseSec = null,
    [property: JsonPropertyName("source")] JsonElement? Source = null)
{
    public (SourceSuppliedConfig? Config, bool SourceFieldDropped) ToDomain()
    {
        bool sourcePresent = Source.HasValue
            && Source.Value.ValueKind != JsonValueKind.Undefined
            && Source.Value.ValueKind != JsonValueKind.Null;

        SourceSuppliedConfig config = new(
            Name: Normalize(Name),
            Executable: Normalize(Executable),
            CheckMinutes: CheckMinutes,
            ShutdownTimeoutSec: ShutdownTimeoutSec,
            KillAfterSec: KillAfterSec,
            RestartAttempts: RestartAttempts,
            RestartPauseSec: RestartPauseSec);

        return (config, sourcePresent);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
