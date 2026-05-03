using System.Text.Json.Serialization;

namespace Husky.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(MessageEnvelope))]
[JsonSerializable(typeof(HelloPayload))]
[JsonSerializable(typeof(HelloPreferences))]
[JsonSerializable(typeof(WelcomePayload))]
[JsonSerializable(typeof(PongPayload))]
[JsonSerializable(typeof(ShutdownPayload))]
[JsonSerializable(typeof(ShutdownProgressPayload))]
[JsonSerializable(typeof(UpdateStatusPayload))]
[JsonSerializable(typeof(UpdateAvailablePayload))]
[JsonSerializable(typeof(SetUpdateModePayload))]
[JsonSerializable(typeof(UpdateModeAckPayload))]
public sealed partial class HuskyJsonContext : JsonSerializerContext;
