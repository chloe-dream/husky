namespace Husky;

internal sealed record AppProcessOptions(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment = null);
