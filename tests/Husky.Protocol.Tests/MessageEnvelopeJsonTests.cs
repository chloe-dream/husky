using System.Text.Json;
using Husky.Protocol;

namespace Husky.Protocol.Tests;

public sealed class MessageEnvelopeJsonTests
{
    [Fact]
    public void Hello_envelope_matches_spec_wire_shape()
    {
        HelloPayload payload = new(
            ProtocolVersion: 1,
            AppVersion: "1.4.2",
            AppName: "umbrella-bot",
            Pid: 4218);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.HelloPayload);
        MessageEnvelope envelope = new()
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Type = MessageTypes.Hello,
            Data = data,
        };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal(
            """{"id":"11111111-1111-1111-1111-111111111111","type":"hello","data":{"protocolVersion":1,"appVersion":"1.4.2","appName":"umbrella-bot","pid":4218}}""",
            json);
    }

    [Fact]
    public void Welcome_envelope_with_null_reason_omits_the_field()
    {
        WelcomePayload payload = new(
            ProtocolVersion: 1,
            LauncherVersion: "1.0.0",
            Accepted: true,
            Reason: null);

        string json = JsonSerializer.Serialize(payload, HuskyJsonContext.Default.WelcomePayload);

        Assert.Equal(
            """{"protocolVersion":1,"launcherVersion":"1.0.0","accepted":true}""",
            json);
    }

    [Fact]
    public void Heartbeat_envelope_has_only_a_type_field()
    {
        MessageEnvelope envelope = new() { Type = MessageTypes.Heartbeat };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal("""{"type":"heartbeat"}""", json);
    }

    [Fact]
    public void Ping_envelope_carries_id_and_type_only()
    {
        MessageEnvelope envelope = new()
        {
            Id = "44444444-4444-4444-4444-444444444444",
            Type = MessageTypes.Ping,
        };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal(
            """{"id":"44444444-4444-4444-4444-444444444444","type":"ping"}""",
            json);
    }

    [Fact]
    public void Welcome_with_rejection_keeps_reason_in_the_wire_shape()
    {
        // The rejection path is how the launcher refuses an app on protocol
        // version mismatch (LEASH §3.6). Pin the wire shape so a future
        // refactor cannot silently drop the reason field.
        WelcomePayload payload = new(
            ProtocolVersion: 1,
            LauncherVersion: "1.0.0",
            Accepted: false,
            Reason: "protocol version mismatch: launcher=1, app=2");

        string json = JsonSerializer.Serialize(payload, HuskyJsonContext.Default.WelcomePayload);

        Assert.Equal(
            """{"protocolVersion":1,"launcherVersion":"1.0.0","accepted":false,"reason":"protocol version mismatch: launcher=1, app=2"}""",
            json);
    }

    [Fact]
    public void ShutdownAck_envelope_carries_id_and_replyTo()
    {
        MessageEnvelope envelope = new()
        {
            Id = "22222222-2222-2222-2222-222222222222",
            ReplyTo = "33333333-3333-3333-3333-333333333333",
            Type = MessageTypes.ShutdownAck,
        };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal(
            """{"id":"22222222-2222-2222-2222-222222222222","replyTo":"33333333-3333-3333-3333-333333333333","type":"shutdown-ack"}""",
            json);
    }

    [Fact]
    public void Pong_payload_round_trips_through_json()
    {
        Dictionary<string, JsonElement> details = new()
        {
            ["queue"] = JsonSerializer.SerializeToElement(3),
            ["guilds"] = JsonSerializer.SerializeToElement(12),
        };
        PongPayload original = new("healthy", details);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.PongPayload);
        PongPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.PongPayload);

        Assert.Equal("""{"status":"healthy","details":{"queue":3,"guilds":12}}""", json);
        Assert.NotNull(parsed);
        Assert.Equal("healthy", parsed!.Status);
        Assert.NotNull(parsed.Details);
        Assert.Equal(3, parsed.Details!["queue"].GetInt32());
        Assert.Equal(12, parsed.Details["guilds"].GetInt32());
    }

    [Fact]
    public void Shutdown_payload_round_trips_through_json()
    {
        ShutdownPayload original = new(Reason: "update", TimeoutSeconds: 60);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.ShutdownPayload);
        ShutdownPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.ShutdownPayload);

        Assert.Equal("""{"reason":"update","timeoutSeconds":60}""", json);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ShutdownProgress_payload_round_trips_through_json()
    {
        ShutdownProgressPayload original = new("flushing queue (3 items left)");

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.ShutdownProgressPayload);
        ShutdownProgressPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.ShutdownProgressPayload);

        Assert.Equal("""{"message":"flushing queue (3 items left)"}""", json);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Hello_payload_round_trips_through_envelope()
    {
        HelloPayload original = new(1, "2.0.0-rc.1", "fishbowl", 12345);
        JsonElement data = JsonSerializer.SerializeToElement(original, HuskyJsonContext.Default.HelloPayload);
        MessageEnvelope envelope = new() { Type = MessageTypes.Hello, Data = data };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);
        MessageEnvelope? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.MessageEnvelope);

        Assert.NotNull(parsed);
        Assert.Equal(MessageTypes.Hello, parsed!.Type);
        Assert.NotNull(parsed.Data);

        HelloPayload? parsedPayload = parsed.Data!.Value.Deserialize(HuskyJsonContext.Default.HelloPayload);
        Assert.Equal(original, parsedPayload);
    }

    [Fact]
    public void Unknown_message_type_still_parses_into_envelope()
    {
        // Per LEASH §3.6: additive fields and unknown message types must not break
        // older readers — they get an envelope with a type they do not recognize
        // and the consumer decides what to do.
        const string json = """{"type":"future-message","data":{"foo":42}}""";

        MessageEnvelope? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.MessageEnvelope);

        Assert.NotNull(parsed);
        Assert.Equal("future-message", parsed!.Type);
        Assert.NotNull(parsed.Data);
        Assert.Equal(42, parsed.Data!.Value.GetProperty("foo").GetInt32());
    }

    [Fact]
    public void Missing_type_field_throws_on_deserialize()
    {
        const string json = """{"id":"abc","data":{}}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(json, HuskyJsonContext.Default.MessageEnvelope));
    }

    [Fact]
    public void Additive_unknown_fields_in_payload_are_ignored()
    {
        // A future protocol version may add fields to existing payloads. Older
        // readers must drop them silently rather than fail.
        const string json = """{"protocolVersion":1,"appVersion":"1.0.0","appName":"x","pid":1,"futureField":"ignored"}""";

        HelloPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.HelloPayload);

        Assert.NotNull(parsed);
        Assert.Equal("x", parsed!.AppName);
    }
}
