using System.Text;
using Husky.Protocol;

namespace Husky.Protocol.Tests;

public sealed class MessageWriterTests
{
    [Fact]
    public async Task Writes_a_single_message_with_lf_terminator_and_no_bom()
    {
        using MemoryStream stream = new();
        await using MessageWriter writer = new(stream, leaveOpen: true);

        await writer.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });

        byte[] bytes = stream.ToArray();
        string text = Encoding.UTF8.GetString(bytes);

        Assert.Equal("""{"type":"heartbeat"}""" + "\n", text);
        Assert.NotEqual(0xEF, bytes[0]); // no UTF-8 BOM in the leading byte
    }

    [Fact]
    public async Task Writes_multiple_messages_separated_by_lf()
    {
        using MemoryStream stream = new();
        await using MessageWriter writer = new(stream, leaveOpen: true);

        await writer.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });
        await writer.WriteAsync(new MessageEnvelope { Id = "x", Type = MessageTypes.Ping });

        string text = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Equal(
            """{"type":"heartbeat"}""" + "\n" +
            """{"id":"x","type":"ping"}""" + "\n",
            text);
    }

    [Fact]
    public async Task Roundtrips_through_writer_and_reader()
    {
        using MemoryStream stream = new();
        await using (MessageWriter writer = new(stream, leaveOpen: true))
        {
            await writer.WriteAsync(new MessageEnvelope
            {
                Id = "abc",
                ReplyTo = "xyz",
                Type = MessageTypes.Welcome,
            });
        }

        stream.Position = 0;
        using MessageReader reader = new(stream);
        MessageEnvelope? envelope = await reader.ReadAsync();

        Assert.NotNull(envelope);
        Assert.Equal("abc", envelope!.Id);
        Assert.Equal("xyz", envelope.ReplyTo);
        Assert.Equal(MessageTypes.Welcome, envelope.Type);
    }

    [Fact]
    public async Task DisposeAsync_with_leaveOpen_keeps_stream_alive()
    {
        MemoryStream stream = new();
        MessageWriter writer = new(stream, leaveOpen: true);

        await writer.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });
        await writer.DisposeAsync();

        // Should still be usable after writer disposal.
        stream.Position = 0;
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task DisposeAsync_without_leaveOpen_disposes_stream()
    {
        MemoryStream stream = new();
        MessageWriter writer = new(stream); // leaveOpen defaults to false

        await writer.WriteAsync(new MessageEnvelope { Type = MessageTypes.Heartbeat });
        await writer.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(0));
    }
}
