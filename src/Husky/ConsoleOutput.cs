using System.Text.RegularExpressions;
using Retro.Crt;

namespace Husky;

/// <summary>
/// The launcher's single console-output abstraction. Every user-visible line
/// — launcher events, hosted-app stdout/stderr, pipe-debug traces — flows
/// through one of the four facades here so the LEASH §10.3 line format
/// (timestamp + 8-char source + message with status-word highlights) and
/// the LEASH §10.7 live-widget gate are applied consistently.
/// </summary>
internal static partial class ConsoleOutput
{
    private const int SourceWidth = 8;
    internal const int LiveWidgetQueueCap = 256;

    private static readonly object Lock = new();
    private static bool widgetActive;
    private static readonly Queue<QueuedLine> Queue = new();
    private static int droppedDuringWidget;

    /// <summary>
    /// Logs a launcher line tagged <c>husky</c> in cyan.
    /// </summary>
    /// <param name="message">The line to render. Status words (<c>up</c>,
    /// <c>down</c>, <c>healthy</c>, <c>degraded</c>, <c>unhealthy</c>,
    /// <c>growling</c>) are auto-highlighted.</param>
    /// <param name="force">When <c>true</c>, the line bypasses the
    /// live-widget queue (LEASH §10.7) and writes immediately even if a
    /// <see cref="ProgressBar"/> or <see cref="Spinner"/> is currently
    /// holding the line — at the cost of briefly clobbering the widget's
    /// frame. Reserved for error escalations that must not be swallowed
    /// behind a still-running spinner. Decorative status lines pass
    /// <c>false</c> (the default).</param>
    public static void Husky(string message, bool force = false) =>
        Render("husky", Color.LightCyan, message, force);

    /// <summary>Logs a line from the hosted app's stdout in green. Always queued behind an active live widget.</summary>
    public static void AppOut(string message) => Render("app",  Color.LightGreen, message, force: false);

    /// <summary>Logs a line from the hosted app's stderr in red. Always queued behind an active live widget.</summary>
    public static void AppErr(string message) => Render("app",  Color.LightRed,   message, force: false);

    /// <summary>Logs a verbose-debug pipe trace in dark gray. Always queued behind an active live widget.</summary>
    public static void Pipe(string message)   => Render("pipe", Color.DarkGray,   message, force: false);

    /// <summary>
    /// Take the cursor for an in-place Crt widget (ProgressBar, Spinner). While
    /// the returned scope is alive, log lines from app/launcher are buffered
    /// (cap <see cref="LiveWidgetQueueCap"/>, oldest dropped) and flushed when
    /// the widget disposes. Pass <c>force: true</c> to <see cref="Husky"/> for
    /// error escalations that must be visible immediately even at the cost of
    /// briefly clobbering the active widget's frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A second concurrent widget would shred the cursor; only one widget at a
    /// time is supported by contract.
    /// </exception>
    public static IDisposable BeginLiveWidget()
    {
        lock (Lock)
        {
            if (widgetActive)
                throw new InvalidOperationException(
                    "ConsoleOutput already has an active live widget — only one at a time.");
            widgetActive = true;
        }
        return new WidgetScope();
    }

    private static void Render(string source, Color sourceColor, string message, bool force)
    {
        var when = DateTime.Now;
        lock (Lock)
        {
            if (widgetActive && !force)
            {
                if (Queue.Count >= LiveWidgetQueueCap)
                {
                    Queue.Dequeue();
                    droppedDuringWidget++;
                }
                Queue.Enqueue(new QueuedLine(when, source, sourceColor, message));
                return;
            }
        }
        WriteLine(when, source, sourceColor, message);
    }

    private static void WriteLine(DateTime when, string source, Color sourceColor, string message)
    {
        var line = BuildLine(when, source, sourceColor, message);
        for (var i = 0; i < line.Count; i++)
        {
            var seg = line[i];
            if (seg.Color is { } c)
            {
                using (Crt.WithStyle(fg: c)) Crt.Write(seg.Text);
            }
            else
            {
                Crt.Write(seg.Text);
            }
        }
        Crt.WriteLine();
    }

    /// <summary>
    /// Build the rendered line as a list of (text, color) segments. Pure and
    /// allocation-bounded; the test suite drives this directly to verify the
    /// timestamp format, source padding, and status-word highlighting without
    /// having to capture <see cref="Console.Out"/>.
    /// </summary>
    internal static IReadOnlyList<LineSegment> BuildLine(
        DateTime when, string source, Color sourceColor, string message)
    {
        var timestamp = when.ToString("HH:mm:ss");
        var paddedSource = source.Length >= SourceWidth ? source : source.PadRight(SourceWidth);

        var segments = new List<LineSegment>(8)
        {
            new(timestamp, Color.DarkGray),
            new("  "),
            new(paddedSource, sourceColor),
            new("  "),
        };

        AppendMessage(segments, message);
        return segments;
    }

    private static void AppendMessage(List<LineSegment> segments, string message)
    {
        var matches = StatusWordRegex().Matches(message);
        if (matches.Count == 0)
        {
            segments.Add(new(message));
            return;
        }

        var pos = 0;
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.Index > pos) segments.Add(new(message[pos..m.Index]));
            segments.Add(new(m.Value, ColorForStatusWord(m.Value)));
            pos = m.Index + m.Length;
        }
        if (pos < message.Length) segments.Add(new(message[pos..]));
    }

    private static Color ColorForStatusWord(string word) => word switch
    {
        "up"        => Color.LightGreen,
        "down"      => Color.LightRed,
        "healthy"   => Color.LightGreen,
        "degraded"  => Color.Yellow,
        "unhealthy" => Color.LightRed,
        "growling"  => Color.Yellow,
        _           => Color.LightGray,
    };

    [GeneratedRegex(@"\b(up|down|healthy|degraded|unhealthy|growling)\b")]
    private static partial Regex StatusWordRegex();

    internal readonly record struct LineSegment(string Text, Color? Color = null);

    private readonly record struct QueuedLine(
        DateTime When, string Source, Color SourceColor, string Message);

    private sealed class WidgetScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            QueuedLine[] toFlush;
            int dropped;
            lock (Lock)
            {
                toFlush = Queue.ToArray();
                Queue.Clear();
                dropped = droppedDuringWidget;
                droppedDuringWidget = 0;
                widgetActive = false;
            }

            for (var i = 0; i < toFlush.Length; i++)
            {
                var l = toFlush[i];
                WriteLine(l.When, l.Source, l.SourceColor, l.Message);
            }

            if (dropped > 0)
                WriteLine(
                    DateTime.Now, "husky", Color.LightCyan,
                    $"… {dropped} app line(s) elided while widget held the line.");
        }
    }
}
