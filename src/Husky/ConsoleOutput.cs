using System.Text.RegularExpressions;
using Retro.Crt;

namespace Husky;

/// <summary>
/// The launcher's single console-output abstraction. Every user-visible line
/// — launcher events, hosted-app stdout/stderr, pipe-debug traces — flows
/// through one of the four facades here so the LEASH §10 line format
/// (timestamp + 8-char source + message with status-word highlights) and
/// the live-widget gate are applied consistently.
///
/// Rendering itself is delegated to an <see cref="IConsoleSink"/>. The
/// default <see cref="CrtConsoleSink"/> drives the legacy line-mode
/// (LEASH §10.3): segments rendered via <c>Crt.Write</c> with per-run
/// colours, plus a process-global widget gate that buffers log lines
/// behind an active <c>ProgressBar</c> or <c>Spinner</c>. In TUI mode
/// (LEASH §10.4) <c>HuskyApp</c> swaps in a sink that pushes lines into a
/// <c>Retro.Crt.Tui.LogViewer</c>; live-widget creation is suppressed
/// because the LogViewer doesn't share cursor state with anything.
/// </summary>
internal static partial class ConsoleOutput
{
    private const int SourceWidth = 8;
    internal const int LiveWidgetQueueCap = 256;

    private static IConsoleSink sink = new CrtConsoleSink();

    /// <summary>
    /// Replace the active sink. Intended to be called once during startup —
    /// <c>HuskyApp</c> installs its TUI sink before the first log line, and
    /// the launcher tears it down only on process exit. Calling this with a
    /// fresh sink while a live widget is active is undefined behaviour
    /// (the in-flight queue would be stranded in the previous sink).
    /// </summary>
    public static void SetSink(IConsoleSink newSink)
    {
        ArgumentNullException.ThrowIfNull(newSink);
        sink = newSink;
    }

    /// <summary>
    /// Restore the default Crt-based sink. Tests use this to undo a swap.
    /// </summary>
    public static void ResetSink() => sink = new CrtConsoleSink();

    /// <summary>
    /// Logs a launcher line tagged <c>husky</c> in cyan.
    /// </summary>
    /// <param name="message">The line to render. Status words (<c>up</c>,
    /// <c>down</c>, <c>healthy</c>, <c>degraded</c>, <c>unhealthy</c>,
    /// <c>growling</c>) are auto-highlighted in line mode; in TUI mode the
    /// whole line takes the source colour.</param>
    /// <param name="force">When <c>true</c>, the line bypasses the
    /// live-widget queue and writes immediately even if a <see cref="ProgressBar"/>
    /// or <see cref="Spinner"/> is currently holding the line — at the cost
    /// of briefly clobbering the widget's frame. If a widget is actually
    /// active when the escalation fires, a <see cref="Crt.Bell"/> is also
    /// emitted (pipe-safe via <c>IsInteractive</c>) so the user notices the
    /// interrupt audibly. Reserved for error escalations that must not be
    /// swallowed behind a still-running spinner. Decorative status lines
    /// pass <c>false</c> (the default). Has no special effect in TUI mode
    /// because the LogViewer doesn't have a cursor-sharing widget; the
    /// line just appears at the tail.</param>
    /// <param name="messageColor">When set, applies to every plain text
    /// run in <paramref name="message"/> in line mode. Status-word
    /// highlights still override per word in line mode. TUI mode ignores
    /// this — the whole line takes the source colour.</param>
    public static void Husky(string message, bool force = false, Color? messageColor = null) =>
        sink.Append(DateTime.Now, "husky", Color.LightCyan, message, messageColor, force);

    /// <summary>Logs a line from the hosted app's stdout in green. Always queued behind an active live widget.</summary>
    public static void AppOut(string message) =>
        sink.Append(DateTime.Now, "app", Color.LightGreen, message, messageColor: null, force: false);

    /// <summary>Logs a line from the hosted app's stderr in red. Always queued behind an active live widget.</summary>
    public static void AppErr(string message) =>
        sink.Append(DateTime.Now, "app", Color.LightRed, message, messageColor: null, force: false);

    /// <summary>Logs a verbose-debug pipe trace in dark gray. Always queued behind an active live widget.</summary>
    public static void Pipe(string message) =>
        sink.Append(DateTime.Now, "pipe", Color.DarkGray, message, messageColor: null, force: false);

    /// <summary>
    /// Take the cursor for an in-place Crt widget (ProgressBar, Spinner). While
    /// the returned scope is alive, log lines from app/launcher are buffered
    /// (cap <see cref="LiveWidgetQueueCap"/>, oldest dropped) and flushed when
    /// the widget disposes. Pass <c>force: true</c> to <see cref="Husky"/> for
    /// error escalations that must be visible immediately even at the cost of
    /// briefly clobbering the active widget's frame.
    ///
    /// In TUI mode this returns a no-op scope that also redirects Crt's
    /// underlying <see cref="TextWriter"/> to <see cref="TextWriter.Null"/> so
    /// callers' Spinner/ProgressBar frames vanish silently rather than
    /// painting garbage onto the alt-screen. The caller's regular
    /// <see cref="Husky"/> log lines reach the LogViewer untouched.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// In line mode, a second concurrent widget would shred the cursor; only
    /// one widget at a time is supported by contract.
    /// </exception>
    public static IDisposable BeginLiveWidget() => sink.BeginLiveWidget();

    /// <summary>
    /// Build the rendered line as a list of (text, color) segments. Pure and
    /// allocation-bounded; the test suite drives this directly to verify the
    /// timestamp format, source padding, and status-word highlighting without
    /// having to capture <see cref="Console.Out"/>.
    /// </summary>
    internal static IReadOnlyList<LineSegment> BuildLine(
        DateTime when, string source, Color sourceColor, string message, Color? messageColor = null)
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

        AppendMessage(segments, message, messageColor);
        return segments;
    }

    private static void AppendMessage(List<LineSegment> segments, string message, Color? messageColor)
    {
        var matches = StatusWordRegex().Matches(message);
        if (matches.Count == 0)
        {
            segments.Add(new(message, messageColor));
            return;
        }

        var pos = 0;
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.Index > pos) segments.Add(new(message[pos..m.Index], messageColor));
            segments.Add(new(m.Value, ColorForStatusWord(m.Value)));
            pos = m.Index + m.Length;
        }
        if (pos < message.Length) segments.Add(new(message[pos..], messageColor));
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

    /// <summary>
    /// A renderer-and-gate for log lines. Implementations decide both how
    /// a line is painted (multi-color Crt segments vs. single-color
    /// LogViewer entry) and whether a Spinner/ProgressBar should be allowed
    /// to take the cursor for a scope.
    /// </summary>
    internal interface IConsoleSink
    {
        /// <summary>Render a single log line.</summary>
        /// <param name="force">In line mode: bypass the widget queue and
        /// optionally beep. In TUI mode: ignored (LogViewer has no widget
        /// to bypass).</param>
        void Append(
            DateTime when, string source, Color sourceColor, string message,
            Color? messageColor, bool force);

        /// <summary>Reserve the screen for a single in-place widget. See
        /// <see cref="ConsoleOutput.BeginLiveWidget"/> for semantics.</summary>
        IDisposable BeginLiveWidget();
    }

    /// <summary>
    /// Default sink: renders lines via <c>Crt.Write</c> with the per-segment
    /// colour palette from <see cref="BuildLine"/>, and gates concurrent
    /// in-place widgets with a process-global queue.
    /// </summary>
    private sealed class CrtConsoleSink : IConsoleSink
    {
        private readonly object lockObj = new();
        private bool widgetActive;
        private readonly Queue<QueuedLine> queue = new();
        private int droppedDuringWidget;

        public void Append(
            DateTime when, string source, Color sourceColor, string message,
            Color? messageColor, bool force)
        {
            bool escalated;
            lock (lockObj)
            {
                if (widgetActive && !force)
                {
                    if (queue.Count >= LiveWidgetQueueCap)
                    {
                        queue.Dequeue();
                        droppedDuringWidget++;
                    }
                    queue.Enqueue(new QueuedLine(when, source, sourceColor, message, messageColor));
                    return;
                }
                escalated = widgetActive && force;
            }
            if (escalated) Crt.Bell();
            WriteLineNow(when, source, sourceColor, message, messageColor);
        }

        public IDisposable BeginLiveWidget()
        {
            lock (lockObj)
            {
                if (widgetActive)
                    throw new InvalidOperationException(
                        "ConsoleOutput already has an active live widget — only one at a time.");
                widgetActive = true;
            }
            return new WidgetScope(this);
        }

        private static void WriteLineNow(
            DateTime when, string source, Color sourceColor, string message, Color? messageColor)
        {
            var line = BuildLine(when, source, sourceColor, message, messageColor);
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

        private readonly record struct QueuedLine(
            DateTime When, string Source, Color SourceColor, string Message, Color? MessageColor);

        private sealed class WidgetScope(CrtConsoleSink owner) : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;

                QueuedLine[] toFlush;
                int dropped;
                lock (owner.lockObj)
                {
                    toFlush = owner.queue.ToArray();
                    owner.queue.Clear();
                    dropped = owner.droppedDuringWidget;
                    owner.droppedDuringWidget = 0;
                    owner.widgetActive = false;
                }

                for (var i = 0; i < toFlush.Length; i++)
                {
                    var l = toFlush[i];
                    WriteLineNow(l.When, l.Source, l.SourceColor, l.Message, l.MessageColor);
                }

                if (dropped > 0)
                    WriteLineNow(
                        DateTime.Now, "husky", Color.LightCyan,
                        $"… {dropped} app line(s) elided while widget held the line.",
                        messageColor: null);
            }
        }
    }
}
