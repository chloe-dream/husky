using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Husky;

/// <summary>
/// LEASH §9.2 — GitHub Releases source. Reads
/// <c>/repos/{repo}/releases/latest</c>, looks for an asset matching the
/// configured pattern (<c>{version}</c> placeholder substituted), and returns
/// an <see cref="UpdateInfo"/> if the release is newer than the current app.
/// </summary>
internal sealed class GitHubUpdateSource(
    HttpClient httpClient,
    Uri apiBase,
    string repo,
    string assetPattern,
    string launcherVersion,
    bool allowPreRelease = false) : IUpdateSource
{
    public const string DefaultApiBase = "https://api.github.com/";
    public const string VersionPlaceholder = "{version}";

    public GitHubUpdateSource(
        HttpClient httpClient, string repo, string assetPattern, string launcherVersion, bool allowPreRelease = false)
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

        string expectedAssetName = assetPattern.Replace(VersionPlaceholder, remoteVersion, StringComparison.Ordinal);
        GitHubAssetDto? asset = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, expectedAssetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new UpdateException(
                $"GitHub release {release.TagName} has no asset matching '{expectedAssetName}'.");

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUrl))
            throw new UpdateException(
                $"GitHub asset '{asset.Name}' has an invalid download URL: '{asset.BrowserDownloadUrl}'.");

        return new UpdateInfo(remoteVersion, downloadUrl, Sha256: null);
    }

    private static string StripVersionPrefix(string tag)
    {
        ReadOnlySpan<char> span = tag.AsSpan().Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V')) span = span[1..];
        return span.ToString();
    }
}
