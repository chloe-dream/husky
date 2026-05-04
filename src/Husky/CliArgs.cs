namespace Husky;

/// <summary>
/// The parsed result of <see cref="CliArgsParser"/>: an optional working
/// directory override (LEASH §5.2) and an optional synthetic source block
/// built from <c>--manifest</c> / <c>--repo</c> / <c>--asset</c>
/// (LEASH §5.2.1). Both may be null when the user invokes a bare
/// <c>husky</c> and relies entirely on the local config.
/// </summary>
internal sealed record CliArgs(
    string? WorkingDirectory,
    SourceConfig? CliSource);
