using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Husky;

/// <summary>
/// LEASH §9.3 — generic HTTP manifest source. Fetches a JSON manifest with
/// <c>{ version, url, sha256 }</c>, compares the version against the current
/// app, and returns an <see cref="UpdateInfo"/> if newer.
/// </summary>
internal sealed class HttpUpdateSource(
    HttpClient httpClient,
    Uri manifestUrl,
    string launcherVersion) : IUpdateSource
{
    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);

        using HttpRequestMessage request = new(HttpMethod.Get, manifestUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Husky", launcherVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        HttpManifestDto? manifest = await response.Content
            .ReadFromJsonAsync(HuskySourceJsonContext.Default.HttpManifestDto, ct)
            .ConfigureAwait(false)
            ?? throw new UpdateException($"Manifest at '{manifestUrl}' was empty.");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new UpdateException("Manifest is missing 'version'.");
        if (string.IsNullOrWhiteSpace(manifest.Url))
            throw new UpdateException("Manifest is missing 'url'.");

        if (!SemanticVersion.TryParse(manifest.Version, out SemanticVersion remote))
            throw new UpdateException($"Manifest 'version' is not valid SemVer: '{manifest.Version}'.");
        if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current)) return null;
        if (remote <= current) return null;

        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out Uri? downloadUrl))
            throw new UpdateException($"Manifest 'url' is not an absolute URL: '{manifest.Url}'.");

        SourceSuppliedConfig? config = null;
        bool sourceFieldDropped = false;
        if (manifest.Config is not null)
        {
            (config, sourceFieldDropped) = manifest.Config.ToDomain();
        }

        return new UpdateInfo(
            Version: manifest.Version,
            DownloadUrl: downloadUrl,
            Sha256: string.IsNullOrWhiteSpace(manifest.Sha256) ? null : manifest.Sha256,
            Config: config,
            SourceFieldDropped: sourceFieldDropped);
    }
}
