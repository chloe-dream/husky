using System.Text.Json;

namespace Husky.Protocol;

public sealed record MessageEnvelope
{
    public string? Id { get; init; }
    public string? ReplyTo { get; init; }
    public required string Type { get; init; }
    public JsonElement? Data { get; init; }
}
