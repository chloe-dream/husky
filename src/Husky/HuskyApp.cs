using System.Collections.Concurrent;
using Retro.Crt;
using Retro.Crt.Tui;
using Retro.Crt.Tui.Widgets;

namespace Husky;

/// <summary>
/// The TUI-mode host (LEASH §10.4). Owns a <see cref="Application"/>, a
/// <see cref="LogViewer"/>, and a <see cref="HuskyChrome"/> that lays out
/// header / log / action-bar. Implements <see cref="ConsoleOutput.IConsoleSink"/>
/// so every <c>ConsoleOutput.Husky</c>/<c>AppOut</c>/<c>AppErr</c>/<c>Pipe</c>
/// call from any thread queues a line that the next render tick drains
/// into the LogViewer.
///
/// Lifecycle:
///   <list type="number">
///     <item><see cref="Run"/> blocks the calling thread on the
///       Application's input/render loop.</item>
///     <item>The launcher runtime executes on a separate task; when it
///       finishes (or the user presses Esc) the chrome's exit callback
///       triggers graceful shutdown and ultimately <see cref="Dismiss"/>
///       which signals the Application to leave the loop.</item>
///   </list>
/// </summary>
internal sealed class HuskyApp : ConsoleOutput.IConsoleSink
{
    // Background threads enqueue here; the UI thread drains during OnDraw.
    // LogViewer is not documented as thread-safe, so all writes go through
    // this hand-off rather than touching its Items list directly.
    private readonly ConcurrentQueue<PendingLine> pending = new();

    private readonly LogViewer logViewer;
    private readonly HuskyChrome chrome;
    private readonly Application application;

    public HuskyApp(string launcherVersion, Action onExitRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        logViewer = new LogViewer
        {
            Foreground     = Color.LightGray,
            Background     = Color.Black,
            ScrollbarTrack = Color.DarkGray,
            ScrollbarThumb = Color.LightCyan,
        };

        chrome = new HuskyChrome(
            launcherVersion: launcherVersion,
            log: logViewer,
            drainPending: DrainPending,
            onExitRequested: onExitRequested);

        application = new Application(chrome);
    }

    /// <summary>
    /// Block the current thread on the Application's input/render loop.
    /// Returns when <see cref="Dismiss"/> is called or when the user
    /// triggers an exit hotkey from the chrome.
    /// </summary>
    public void Run() => application.Run();

    /// <summary>
    /// Signal the input/render loop to leave <see cref="Run"/>. Safe to call
    /// from any thread — <c>Application.Exit</c> stores a boolean that the
    /// loop checks each tick (≤16 ms latency).
    /// </summary>
    public void Dismiss() => application.Exit();

    public void Append(
        DateTime when, string source, Color sourceColor, string message,
        Color? messageColor, bool force)
    {
        // Force/messageColor are line-mode concepts (queue bypass + per-segment
        // colours). In TUI mode the LogViewer takes one colour per line and
        // there is no widget to bypass — both inputs are intentionally ignored.
        _ = force;
        _ = messageColor;
        pending.Enqueue(new PendingLine(when, source, sourceColor, message));
        chrome.MarkDirty();
    }

    public IDisposable BeginLiveWidget()
    {
        // No cursor-sharing in TUI mode. Caller's Spinner.Show/ProgressBar.Start
        // calls would otherwise paint escape sequences directly into the
        // alt-screen and shred our rendering — redirect Crt's underlying
        // sink to /dev/null for the scope's lifetime so those frames vanish.
        // The caller's normal ConsoleOutput.Husky log lines bypass Crt and
        // reach this sink's Append regardless.
        IDisposable crtSink = Crt.WithSink(TextWriter.Null);
        return new SuppressedScope(crtSink);
    }

    private void DrainPending()
    {
        while (pending.TryDequeue(out PendingLine line))
        {
            string formatted = FormatLine(line);
            logViewer.Append(formatted, line.SourceColor);
        }
    }

    private static string FormatLine(PendingLine line)
    {
        // LEASH §10.4: TUI lines carry the same prefix as line mode but in
        // a single source colour. Pad source to match the line-mode width
        // for visual alignment when the user copies the buffer to a file.
        string timestamp = line.When.ToString("HH:mm:ss");
        string source = line.Source.Length >= 8
            ? line.Source
            : line.Source.PadRight(8);
        return $"{timestamp}  {source}  {line.Message}";
    }

    private readonly record struct PendingLine(
        DateTime When, string Source, Color SourceColor, string Message);

    private sealed class SuppressedScope(IDisposable crtSink) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            crtSink.Dispose();
        }
    }
}
