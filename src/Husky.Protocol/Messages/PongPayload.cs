using System.Text.Json;

namespace Husky.Protocol;

public sealed record PongPayload(
    string Status,
    JsonElement? Details);
