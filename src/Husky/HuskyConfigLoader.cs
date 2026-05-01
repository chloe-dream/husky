using System.Text.Json;

namespace Husky;

internal static class HuskyConfigLoader
{
    public const string DefaultFileName = "husky.config.json";

    public static HuskyConfig Load(string path)
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

    internal static HuskyConfig Parse(string json)
    {
        HuskyConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, HuskyConfigJsonContext.Default.HuskyConfig);
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

    private static void Validate(HuskyConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            throw new HuskyConfigException("Config field 'name' is required.");

        if (string.IsNullOrWhiteSpace(config.Executable))
            throw new HuskyConfigException("Config field 'executable' is required.");

        if (config.Source is null)
            throw new HuskyConfigException("Config field 'source' is required.");

        ValidateSource(config.Source);

        if (config.CheckMinutes < HuskyConfig.MinimumCheckMinutes)
            throw new HuskyConfigException(
                $"Config field 'checkMinutes' must be at least {HuskyConfig.MinimumCheckMinutes}; got {config.CheckMinutes}.");

        if (config.ShutdownTimeoutSec <= 0)
            throw new HuskyConfigException(
                $"Config field 'shutdownTimeoutSec' must be positive; got {config.ShutdownTimeoutSec}.");

        if (config.KillAfterSec < 0)
            throw new HuskyConfigException(
                $"Config field 'killAfterSec' must be non-negative; got {config.KillAfterSec}.");

        if (config.RestartAttempts < 0)
            throw new HuskyConfigException(
                $"Config field 'restartAttempts' must be non-negative; got {config.RestartAttempts}.");

        if (config.RestartPauseSec < 0)
            throw new HuskyConfigException(
                $"Config field 'restartPauseSec' must be non-negative; got {config.RestartPauseSec}.");
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
                if (string.IsNullOrWhiteSpace(source.Asset))
                    throw new HuskyConfigException("Config field 'source.asset' is required for type 'github'.");
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
