using System.Text;
using System.Text.Json;
using Husky.Protocol;

namespace Husky.Protocol.Tests;

public sealed class MessageReaderTests
{
    [Fact]
    public async Task Reads_a_single_message()
    {
        using MemoryStream stream = StreamOf("""{"type":"heartbeat"}""" + "\n");
        using MessageReader reader = new(stream);

        MessageEnvelope? envelope = await reader.ReadAsync();

        Assert.NotNull(envelope);
        Assert.Equal(MessageTypes.Heartbeat, envelope!.Type);
    }

    [Fact]
    public async Task Returns_null_at_end_of_stream()
    {
        using MemoryStream stream = new();
        using MessageReader reader = new(stream);

        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task Reads_multiple_messages_in_order()
    {
        using MemoryStream stream = StreamOf(
            """{"type":"heartbeat"}""" + "\n" +
            """{"id":"x","type":"ping"}""" + "\n" +
            """{"replyTo":"x","type":"shutdown-ack"}""" + "\n");
        using MessageReader reader = new(stream);

        MessageEnvelope? first = await reader.ReadAsync();
        MessageEnvelope? second = await reader.ReadAsync();
        MessageEnvelope? third = await reader.ReadAsync();
        MessageEnvelope? fourth = await reader.ReadAsync();

        Assert.Equal(MessageTypes.Heartbeat, first!.Type);
        Assert.Equal(MessageTypes.Ping, second!.Type);
        Assert.Equal("x", second.Id);
        Assert.Equal(MessageTypes.ShutdownAck, third!.Type);
        Assert.Equal("x", third.ReplyTo);
        Assert.Null(fourth);
    }

    [Fact]
    public async Task Skips_empty_lines_between_messages()
    {
        using MemoryStream stream = StreamOf(
            "\n" +
            """{"type":"heartbeat"}""" + "\n" +
            "\n\n" +
            """{"type":"ping"}""" + "\n");
        using MessageReader reader = new(stream);

        MessageEnvelope? first = await reader.ReadAsync();
        MessageEnvelope? second = await reader.ReadAsync();

        Assert.Equal(MessageTypes.Heartbeat, first!.Type);
        Assert.Equal(MessageTypes.Ping, second!.Type);
    }

    [Fact]
    public async Task Throws_on_malformed_json()
    {
        using MemoryStream stream = StreamOf("not-json\n");
        using MessageReader reader = new(stream);

        await Assert.ThrowsAsync<JsonException>(async () => await reader.ReadAsync());
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        using MemoryStream stream = StreamOf("""{"type":"heartbeat"}""" + "\n");
        using MessageReader reader = new(stream);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await reader.ReadAsync(cts.Token));
    }

    [Fact]
    public void LeaveOpen_keeps_underlying_stream_alive()
    {
        MemoryStream stream = new();
        MessageReader reader = new(stream, leaveOpen: true);

        reader.Dispose();

        // If the stream was disposed, accessing CanRead would still be true on
        // a disposed MemoryStream — instead we verify Position is settable.
        stream.Position = 0;
    }

    private static MemoryStream StreamOf(string text) => new(Encoding.UTF8.GetBytes(text));
}
