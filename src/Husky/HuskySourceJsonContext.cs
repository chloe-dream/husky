using System.Text.Json.Serialization;

namespace Husky;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(HttpManifestDto))]
[JsonSerializable(typeof(SourceSuppliedConfigDto))]
internal sealed partial class HuskySourceJsonContext : JsonSerializerContext;
