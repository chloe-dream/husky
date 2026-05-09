using System.Text;
using System.Text.Json;

namespace Husky.Protocol;

/// <summary>
/// Reads JSON Lines messages from a stream. Not thread-safe — pair with a
/// dedicated reader task per pipe.
/// </summary>
public sealed class MessageReader : IDisposable
{
    private readonly StreamReader reader;

    public MessageReader(Stream stream, bool leaveOpen = false)
    {
        reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: -1,
            leaveOpen: leaveOpen);
    }

    /// <summary>
    /// Reads the next message from the stream. Returns <c>null</c> on EOF.
    /// Empty lines between messages are skipped.
    /// </summary>
    public async Task<MessageEnvelope?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return null;
            if (line.Length == 0) continue;

            return JsonSerializer.Deserialize(line, HuskyJsonContext.Default.MessageEnvelope)
                ?? throw new JsonException($"Could not deserialize message envelope from line: {line}");
        }
    }

    public void Dispose() => reader.Dispose();
}
