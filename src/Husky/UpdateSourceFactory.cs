namespace Husky;

internal static class UpdateSourceFactory
{
    /// <summary>
    /// Builds an <see cref="IUpdateSource"/> for the configured source type
    /// (LEASH §9). The caller owns and disposes the supplied
    /// <see cref="HttpClient"/>.
    /// </summary>
    public static IUpdateSource Create(
        SourceConfig config,
        HttpClient httpClient,
        string launcherVersion)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);

        return config.Type switch
        {
            SourceConfig.GitHubType => new GitHubUpdateSource(
                httpClient,
                config.Repo ?? throw new HuskyConfigException("source.repo is required for github."),
                config.Asset ?? throw new HuskyConfigException("source.asset is required for github."),
                launcherVersion),
            SourceConfig.HttpType => new HttpUpdateSource(
                httpClient,
                new Uri(config.Manifest ?? throw new HuskyConfigException("source.manifest is required for http.")),
                launcherVersion),
            _ => throw new HuskyConfigException(
                $"Unknown source type '{config.Type}'. Supported: '{SourceConfig.GitHubType}', '{SourceConfig.HttpType}'.")
        };
    }
}
