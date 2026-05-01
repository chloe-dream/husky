namespace Husky;

internal sealed record SourceConfig(
    string Type,
    string? Repo = null,
    string? Asset = null,
    string? Manifest = null)
{
    public const string GitHubType = "github";
    public const string HttpType = "http";
}
