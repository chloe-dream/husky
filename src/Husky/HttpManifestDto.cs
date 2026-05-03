using System.Text.Json.Serialization;

namespace Husky;

internal sealed record HttpManifestDto(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("config")] SourceSuppliedConfigDto? Config = null);
