namespace Husky;

/// <summary>
/// Parses the launcher's command-line flags per LEASH §5.2.1. Recognises
/// <c>--dir</c>, <c>--manifest</c>, <c>--repo</c> and <c>--asset</c>; any
/// other token, a missing value, a duplicate flag or a conflicting
/// combination throws <see cref="HuskyConfigException"/> so Program.cs can
/// surface it with the usual config-error exit code.
/// </summary>
internal static class CliArgsParser
{
    public static CliArgs Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dir = null;
        string? manifest = null;
        string? repo = null;
        string? asset = null;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--dir":
                    dir = TakeValue(args, ref i, flag, dir);
                    break;
                case "--manifest":
                    manifest = TakeValue(args, ref i, flag, manifest);
                    break;
                case "--repo":
                    repo = TakeValue(args, ref i, flag, repo);
                    break;
                case "--asset":
                    asset = TakeValue(args, ref i, flag, asset);
                    break;
                default:
                    throw new HuskyConfigException($"Unknown command-line argument: '{flag}'.");
            }
        }

        if (manifest is not null && repo is not null)
            throw new HuskyConfigException("Command-line flags '--manifest' and '--repo' are mutually exclusive.");

        if (asset is not null && repo is null)
            throw new HuskyConfigException("Command-line flag '--asset' requires '--repo'.");

        SourceConfig? source = BuildSource(manifest, repo, asset);
        return new CliArgs(WorkingDirectory: dir, CliSource: source);
    }

    private static string TakeValue(string[] args, ref int i, string flag, string? previous)
    {
        if (previous is not null)
            throw new HuskyConfigException($"Command-line flag '{flag}' may only be specified once.");

        if (i + 1 >= args.Length)
            throw new HuskyConfigException($"Command-line flag '{flag}' requires a value.");

        string value = args[++i];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
            throw new HuskyConfigException($"Command-line flag '{flag}' requires a value, got '{value}'.");

        return value;
    }

    private static SourceConfig? BuildSource(string? manifest, string? repo, string? asset)
    {
        if (manifest is not null)
        {
            if (!Uri.TryCreate(manifest, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new HuskyConfigException(
                    $"Command-line flag '--manifest' must be an absolute http(s) URL; got '{manifest}'.");

            return new SourceConfig(Type: SourceConfig.HttpType, Manifest: manifest);
        }

        if (repo is not null)
        {
            if (!IsValidGitHubSlug(repo))
                throw new HuskyConfigException(
                    $"Command-line flag '--repo' must be in the form 'owner/name'; got '{repo}'.");

            return new SourceConfig(Type: SourceConfig.GitHubType, Repo: repo, Asset: asset);
        }

        return null;
    }

    private static bool IsValidGitHubSlug(string slug)
    {
        int slashIndex = slug.IndexOf('/');
        if (slashIndex <= 0 || slashIndex == slug.Length - 1) return false;
        if (slug.IndexOf('/', slashIndex + 1) >= 0) return false;
        return true;
    }
}
