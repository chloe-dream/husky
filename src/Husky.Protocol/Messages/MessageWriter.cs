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
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope, HuskyJsonContext.Default.MessageEnvelope);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(NewLine, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await stream.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
