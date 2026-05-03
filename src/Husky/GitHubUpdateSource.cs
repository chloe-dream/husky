using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Husky;

/// <summary>
/// LEASH §9.2 — GitHub Releases source. Reads
/// <c>/repos/{repo}/releases/latest</c>, looks for an asset matching the
/// configured pattern (<c>{version}</c> placeholder substituted), and returns
/// an <see cref="UpdateInfo"/> if the release is newer than the current app.
/// Also fetches <c>husky.config.json</c> for source-supplied deployment
/// metadata: first as a release asset, then from the repo's default-branch
/// root.
/// </summary>
internal sealed class GitHubUpdateSource(
    HttpClient httpClient,
    Uri apiBase,
    string repo,
    string? assetPattern,
    string launcherVersion,
    bool allowPreRelease = false,
    Uri? rawBase = null) : IUpdateSource
{
    public const string DefaultApiBase = "https://api.github.com/";
    public const string DefaultRawBase = "https://raw.githubusercontent.com/";
    public const string ConfigAssetName = "husky.config.json";
    public const string VersionPlaceholder = "{version}";

    private readonly Uri rawBase = rawBase ?? new Uri(DefaultRawBase);

    public GitHubUpdateSource(
        HttpClient httpClient, string repo, string? assetPattern, string launcherVersion, bool allowPreRelease = false)
        : this(httpClient, new Uri(DefaultApiBase), repo, assetPattern, launcherVersion, allowPreRelease)
    {
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        Uri url = new(apiBase, $"repos/{repo}/releases/latest");
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Husky", launcherVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        GitHubReleaseDto? release = await response.Content
            .ReadFromJsonAsync(HuskySourceJsonContext.Default.GitHubReleaseDto, ct)
            .ConfigureAwait(false)
            ?? throw new UpdateException("GitHub returned an empty response body.");

        if (release.Draft) return null;
        if (release.PreRelease && !allowPreRelease) return null;
        if (string.IsNullOrWhiteSpace(release.TagName)) return null;

        string remoteVersion = StripVersionPrefix(release.TagName);
        if (!SemanticVersion.TryParse(remoteVersion, out SemanticVersion remote)) return null;

        // Even if the GitHub flag isn't set, a SemVer pre-release suffix
        // (-beta, -rc.1) marks an unstable build — same opt-in rule.
        if (remote.PreRelease.Length > 0 && !allowPreRelease) return null;

        if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current)) return null;
        if (remote <= current) return null;

        GitHubAssetDto? asset = SelectAsset(release, remoteVersion);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            string description = string.IsNullOrWhiteSpace(assetPattern)
                ? "any .zip"
                : $"'{assetPattern.Replace(VersionPlaceholder, remoteVersion, StringComparison.Ordinal)}'";
            throw new UpdateException(
                $"GitHub release {release.TagName} has no asset matching {description}.");
        }

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUrl))
            throw new UpdateException(
                $"GitHub asset '{asset.Name}' has an invalid download URL: '{asset.BrowserDownloadUrl}'.");

        (SourceSuppliedConfig? config, bool sourceFieldDropped) =
            await FetchSourceSuppliedConfigAsync(release, ct).ConfigureAwait(false);

        return new UpdateInfo(
            Version: remoteVersion,
            DownloadUrl: downloadUrl,
            Sha256: null,
            Config: config,
            SourceFieldDropped: sourceFieldDropped);
    }

    private async Task<(SourceSuppliedConfig? Config, bool SourceFieldDropped)> FetchSourceSuppliedConfigAsync(
        GitHubReleaseDto release, CancellationToken ct)
    {
        // 1. Try a release asset literally named husky.config.json.
        GitHubAssetDto? configAsset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, ConfigAssetName, StringComparison.OrdinalIgnoreCase));
        if (configAsset is { BrowserDownloadUrl: { Length: > 0 } downloadUrl }
            && Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? assetUri))
        {
            (SourceSuppliedConfig? fromAsset, bool fromAssetDropped) =
                await TryFetchConfigAsync(assetUri, ct).ConfigureAwait(false);
            if (fromAsset is not null) return (fromAsset, fromAssetDropped);
        }

        // 2. Fall back to the repo's default-branch root.
        Uri rawUri = new(rawBase, $"{repo}/HEAD/{ConfigAssetName}");
        return await TryFetchConfigAsync(rawUri, ct).ConfigureAwait(false);
    }

    private async Task<(SourceSuppliedConfig? Config, bool SourceFieldDropped)> TryFetchConfigAsync(
        Uri url, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Husky", launcherVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Network glitch fetching the config is not fatal — we just have no config.
            return (null, false);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return (null, false);
            if (!response.IsSuccessStatusCode) return (null, false);

            SourceSuppliedConfigDto? dto;
            try
            {
                dto = await response.Content
                    .ReadFromJsonAsync(HuskySourceJsonContext.Default.SourceSuppliedConfigDto, ct)
                    .ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed husky.config.json should not block updates — just no source-supplied data.
                return (null, false);
            }

            return dto?.ToDomain() ?? (null, false);
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Picks the release asset matching the configured pattern, or — when
    /// no pattern is configured — the first asset whose name ends with
    /// <c>.zip</c> (LEASH §9.2 fallback). The husky.config.json asset is
    /// excluded from the fallback so it never gets mistaken for the app.
    /// </summary>
    private GitHubAssetDto? SelectAsset(GitHubReleaseDto release, string remoteVersion)
    {
        if (release.Assets is null) return null;

        if (!string.IsNullOrWhiteSpace(assetPattern))
        {
            string expectedName = assetPattern.Replace(VersionPlaceholder, remoteVersion, StringComparison.Ordinal);
            return release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        }

        return release.Assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Name)
            && !string.Equals(a.Name, ConfigAssetName, StringComparison.OrdinalIgnoreCase)
            && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static string StripVersionPrefix(string tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];
        return span.ToString();
    }
}
