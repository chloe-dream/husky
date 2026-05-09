using System.Text.Json;

namespace Husky.Protocol;

/// <summary>
/// Writes JSON Lines messages to a stream. Not thread-safe — pair with a
/// dedicated writer task per pipe.
/// </summary>
public sealed class MessageWriter(Stream stream, bool leaveOpen = false) : IAsyncDisposable
{
    private static readonly byte[] NewLine = [(byte)'\n'];

    public async Task WriteAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        // No FlushAsync after writes: on NamedPipe the Flush blocks until the
        // other side has read all buffered bytes, which deadlocks any flow that
        // writes without an immediate symmetric read. The OS pipe buffer takes
        // care of delivery; readers see complete lines as soon as they read.
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope, HuskyJsonContext.Default.MessageEnvelope);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!leaveOpen)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
