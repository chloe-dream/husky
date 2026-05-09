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

    [Fact]
    public void Hello_with_capabilities_and_preferences_matches_spec_wire_shape()
    {
        // LEASH §3.5.1 — the canonical hello carrying capability tokens and
        // an updateMode preference. Pin the exact wire shape so a refactor
        // cannot silently rearrange or drop fields.
        HelloPayload payload = new(
            ProtocolVersion: 1,
            AppVersion: "1.4.2",
            AppName: "umbrella-bot",
            Pid: 4218,
            Capabilities: [Capabilities.ManualUpdates],
            Preferences: new HelloPreferences(UpdateMode: UpdateModes.Manual));

        string json = JsonSerializer.Serialize(payload, HuskyJsonContext.Default.HelloPayload);

        Assert.Equal(
            """{"protocolVersion":1,"appVersion":"1.4.2","appName":"umbrella-bot","pid":4218,"capabilities":["manual-updates"],"preferences":{"updateMode":"manual"}}""",
            json);
    }

    [Fact]
    public void Hello_without_capabilities_or_preferences_omits_those_fields()
    {
        // Backward compat: a pre-update-protocol app sends only the original
        // four fields; the wire output must still match the v1.0 baseline.
        HelloPayload payload = new(
            ProtocolVersion: 1,
            AppVersion: "1.0.0",
            AppName: "legacy-app",
            Pid: 9999);

        string json = JsonSerializer.Serialize(payload, HuskyJsonContext.Default.HelloPayload);

        Assert.Equal(
            """{"protocolVersion":1,"appVersion":"1.0.0","appName":"legacy-app","pid":9999}""",
            json);
    }

    [Fact]
    public void Hello_round_trips_capabilities_and_preferences()
    {
        HelloPayload original = new(
            ProtocolVersion: 1,
            AppVersion: "2.0.0",
            AppName: "fishbowl",
            Pid: 1234,
            Capabilities: [Capabilities.ManualUpdates],
            Preferences: new HelloPreferences(UpdateMode: UpdateModes.Manual));

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.HelloPayload);
        HelloPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.HelloPayload);

        Assert.NotNull(parsed);
        Assert.Equal(original.Capabilities, parsed!.Capabilities);
        Assert.Equal(UpdateModes.Manual, parsed.Preferences?.UpdateMode);
    }

    [Fact]
    public void Welcome_with_capabilities_includes_them_in_wire_shape()
    {
        WelcomePayload payload = new(
            ProtocolVersion: 1,
            LauncherVersion: "1.0.0",
            Accepted: true,
            Reason: null,
            Capabilities: [Capabilities.ManualUpdates]);

        string json = JsonSerializer.Serialize(payload, HuskyJsonContext.Default.WelcomePayload);

        Assert.Equal(
            """{"protocolVersion":1,"launcherVersion":"1.0.0","accepted":true,"capabilities":["manual-updates"]}""",
            json);
    }

    [Fact]
    public void UpdateStatus_payload_round_trips_through_json()
    {
        UpdateStatusPayload original = new(
            Available: true,
            CurrentVersion: "1.4.2",
            NewVersion: "1.4.3",
            DownloadSizeBytes: 6918432);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.UpdateStatusPayload);
        UpdateStatusPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.UpdateStatusPayload);

        Assert.Equal(
            """{"available":true,"currentVersion":"1.4.2","newVersion":"1.4.3","downloadSizeBytes":6918432}""",
            json);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void UpdateStatus_with_no_update_omits_optional_fields()
    {
        UpdateStatusPayload original = new(
            Available: false,
            CurrentVersion: "1.4.2");

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.UpdateStatusPayload);

        Assert.Equal(
            """{"available":false,"currentVersion":"1.4.2"}""",
            json);
    }

    [Fact]
    public void UpdateAvailable_payload_round_trips_through_json()
    {
        UpdateAvailablePayload original = new(
            CurrentVersion: "1.4.2",
            NewVersion: "1.4.3",
            DownloadSizeBytes: 6918432);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.UpdateAvailablePayload);
        UpdateAvailablePayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.UpdateAvailablePayload);

        Assert.Equal(
            """{"currentVersion":"1.4.2","newVersion":"1.4.3","downloadSizeBytes":6918432}""",
            json);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void UpdateCheck_envelope_carries_id_and_type_only()
    {
        // Mirrors ping (§3.5.4). No payload — purely a request marker.
        MessageEnvelope envelope = new()
        {
            Id = "55555555-5555-5555-5555-555555555555",
            Type = MessageTypes.UpdateCheck,
        };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal(
            """{"id":"55555555-5555-5555-5555-555555555555","type":"update-check"}""",
            json);
    }

    [Fact]
    public void UpdateNow_envelope_has_only_a_type_field()
    {
        // Fire-and-forget trigger (§3.5.12). No id, no data.
        MessageEnvelope envelope = new() { Type = MessageTypes.UpdateNow };

        string json = JsonSerializer.Serialize(envelope, HuskyJsonContext.Default.MessageEnvelope);

        Assert.Equal("""{"type":"update-now"}""", json);
    }

    [Fact]
    public void SetUpdateMode_payload_round_trips_through_json()
    {
        SetUpdateModePayload original = new(Mode: UpdateModes.Manual);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.SetUpdateModePayload);
        SetUpdateModePayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.SetUpdateModePayload);

        Assert.Equal("""{"mode":"manual"}""", json);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void UpdateModeAck_payload_round_trips_through_json()
    {
        UpdateModeAckPayload original = new(Mode: UpdateModes.Auto);

        string json = JsonSerializer.Serialize(original, HuskyJsonContext.Default.UpdateModeAckPayload);
        UpdateModeAckPayload? parsed = JsonSerializer.Deserialize(json, HuskyJsonContext.Default.UpdateModeAckPayload);

        Assert.Equal("""{"mode":"auto"}""", json);
        Assert.Equal(original, parsed);
    }
}
