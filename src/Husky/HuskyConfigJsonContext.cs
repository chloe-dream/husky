using System.Text.Json;
using System.Text.Json.Serialization;

namespace Husky;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(HuskyConfig))]
[JsonSerializable(typeof(SourceConfig))]
internal sealed partial class HuskyConfigJsonContext : JsonSerializerContext;
