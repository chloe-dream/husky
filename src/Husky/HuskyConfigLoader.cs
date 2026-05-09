using System.Text.Json;

namespace Husky;

internal static class HuskyConfigLoader
{
    public const string DefaultFileName = "husky.config.json";

    public static LocalHuskyConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new HuskyConfigException($"Config file not found: '{path}'.");

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new HuskyConfigException($"Could not read config file '{path}': {ex.Message}", ex);
        }

        return Parse(content);
    }

    /// <summary>
    /// Returns null when the file is absent — CLI source flags (LEASH §5.2.1)
    /// can stand in for the local config. Throws on parse errors so a broken
    /// file is never silently ignored, even when CLI supplies a source.
    /// </summary>
    public static LocalHuskyConfig? LoadIfPresent(string path) =>
        File.Exists(path) ? Load(path) : null;

    internal static LocalHuskyConfig Parse(string json)
    {
        LocalHuskyConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, HuskyConfigJsonContext.Default.LocalHuskyConfig);
        }
        catch (JsonException ex)
        {
            throw new HuskyConfigException($"Config is not valid JSON: {ex.Message}", ex);
        }

        if (config is null)
            throw new HuskyConfigException("Config is empty.");

        Validate(config);
        return config;
    }

    private static void Validate(LocalHuskyConfig config)
    {
        // Source may be null on disk: CLI flags (--manifest/--repo) can fill
        // it in (LEASH §5.2.1). Validate only when present.
        if (config.Source is not null)
            ValidateSource(config.Source);
    }

    private static void ValidateSource(SourceConfig source)
    {
        if (string.IsNullOrWhiteSpace(source.Type))
            throw new HuskyConfigException("Config field 'source.type' is required.");

        switch (source.Type)
        {
            case SourceConfig.GitHubType:
                if (string.IsNullOrWhiteSpace(source.Repo))
                    throw new HuskyConfigException("Config field 'source.repo' is required for type 'github'.");
                // source.asset is optional — if omitted, the provider picks
                // the first .zip asset (LEASH §9.2).
                break;

            case SourceConfig.HttpType:
                if (string.IsNullOrWhiteSpace(source.Manifest))
                    throw new HuskyConfigException("Config field 'source.manifest' is required for type 'http'.");
                if (!Uri.TryCreate(source.Manifest, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new HuskyConfigException(
                        $"Config field 'source.manifest' must be an absolute http(s) URL; got '{source.Manifest}'.");
                break;

            default:
                throw new HuskyConfigException(
                    $"Config field 'source.type' must be '{SourceConfig.GitHubType}' or '{SourceConfig.HttpType}'; got '{source.Type}'.");
        }
    }
}
