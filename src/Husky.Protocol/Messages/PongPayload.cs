using System.Text.Json;

namespace Husky.Protocol;

public sealed record PongPayload(
    string Status,
    IReadOnlyDictionary<string, JsonElement>? Details);
