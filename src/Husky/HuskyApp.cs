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
/// call from any thread queues an op that the next render tick drains
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
    // Background threads enqueue ops here; the UI thread drains during OnDraw.
    // LogViewer is not documented as thread-safe, so all writes go through
    // this hand-off rather than touching its Items list directly.
    private readonly ConcurrentQueue<PendingOp> pending = new();

    // Tracks whether an in-place line currently owns the LogViewer's tail
    // entry. Read & written only from the UI thread (DrainPending) so no
    // synchronisation is needed; the *enqueue*-side single-instance gate
    // lives on `inPlaceClaimed` below and runs from background threads.
    private bool tailIsInPlace;

    // 0 = no in-place line, 1 = one is open. Flipped via Interlocked so a
    // second concurrent BeginInPlaceLine throws cleanly per LEASH §10.6.
    private int inPlaceClaimed;

    private readonly LogViewer logViewer;
    private readonly HuskyChrome chrome;
    private readonly Application application;
    private readonly string workingDirectory;

    public HuskyApp(
        string launcherVersion,
        string workingDirectory,
        Action onUpdateRequested,
        Action onExitRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(onUpdateRequested);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        this.workingDirectory = workingDirectory;

        logViewer = new LogViewer
        {
            Foreground     = Color.White,
            Background     = Color.Black,
            ScrollbarTrack = Color.DarkGray,
            ScrollbarThumb = Color.LightCyan,
            // §10.7: cap the in-memory buffer so a chatty app can't grow
            // memory unbounded over a long-running session. Oldest entries
            // drop first; the user can still snapshot via [c] before lines
            // age out.
            MaxItems       = 5_000,
        };

        chrome = new HuskyChrome(
            launcherVersion: launcherVersion,
            log: logViewer,
            drainPending: DrainPending,
            // [s] save logs: this side owns the LogViewer snapshot, so the
            // save handler is local to HuskyApp.
            onCopyRequested: CopyLogsToFile,
            onUpdateRequested: onUpdateRequested,
            onExitRequested: onExitRequested);

        application = new Application(chrome);
        // Opt out of mouse capture so the terminal's native click-and-drag
        // text selection keeps working over Husky's alt-screen output.
        // Trade-off: click-to-focus, scrollbar drag, and wheel scrolling
        // stop working; keyboard nav still does. Husky is a passive
        // monitor — operators are far more likely to want to copy a log
        // line than to mouse around the chrome. (Retro.Crt.Tui >= 0.2.0,
        // upstream issue #19.)
        application.MouseCapture = MouseCaptureMode.None;
        // §10.4: 'Default focus: log viewport, pinned to tail.' Without
        // this the first focusable widget wins, which depending on the
        // chrome's layout could be something else after future widgets
        // land. Be explicit.
        application.SetFocus(logViewer);
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
        pending.Enqueue(PendingOp.Append(when, source, sourceColor, message));
        chrome.MarkDirty();
    }

    public void SetAppInfo(string? appName, string? appVersion) =>
        chrome.SetAppInfo(appName, appVersion);

    public void SetHealth(string? status) => chrome.SetHealth(status);

    public void SetCrashRestart(string? message) => chrome.SetCrashRestart(message);

    public void SetUpdateActionState(UpdateActionState state) =>
        chrome.SetUpdateActionState(state);

    /// <summary>
    /// Snapshot the current <see cref="LogViewer"/> contents and write them
    /// to <c>husky-logs-&lt;UTC-timestamp&gt;.txt</c> in the working
    /// directory. Called from the [s] button / hotkey on the UI thread, so
    /// the snapshot is consistent; the actual file IO runs on a background
    /// task to keep the render loop responsive. The result lands as a
    /// husky log line — green on success, yellow on failure — so the user
    /// sees confirmation in the same buffer they just exported.
    /// </summary>
    public void CopyLogsToFile()
    {
        // UI-thread snapshot: LogViewer.Items isn't thread-safe, so copy
        // before we spin up the IO task.
        var snapshot = new List<string>(logViewer.Items.Count);
        foreach (LogEntry entry in logViewer.Items)
            snapshot.Add(entry.Text ?? string.Empty);

        string fileName = $"husky-logs-{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ss}Z.txt";
        // §10.4: save-logs lands in the resolved working directory, not
        // the process's CWD — `--dir` may have pointed Husky elsewhere.
        string path = Path.Combine(workingDirectory, fileName);

        Task.Run(() =>
        {
            try
            {
                File.WriteAllLines(path, snapshot);
                // §10.4: feedback lands as a 3s status-bar replacement,
                // not a log line — the user just dumped the buffer to
                // disk, dropping a confirmation back into the same
                // buffer would be noise.
                chrome.ShowActionBarToast(
                    $"wrote {snapshot.Count} lines -> {fileName}",
                    Color.LightGreen);
            }
            catch (Exception ex)
            {
                chrome.ShowActionBarToast(
                    $"save logs failed: {ex.Message}",
                    Color.Yellow);
            }
        });
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

    public ConsoleOutput.IInPlaceLine BeginInPlaceLine(
        string source, Color sourceColor, string initialMessage)
    {
        if (Interlocked.CompareExchange(ref inPlaceClaimed, 1, 0) != 0)
            throw new InvalidOperationException(
                "ConsoleOutput already has an active in-place line — only one at a time.");

        // The timestamp on an in-place line freezes at start: §10.6 says
        // 'one operation = one frozen start-timestamp during updates'.
        DateTime when = DateTime.Now;
        pending.Enqueue(PendingOp.OpenInPlace(when, source, sourceColor, initialMessage));
        chrome.MarkDirty();

        return new TuiInPlaceLine(this, when, source, sourceColor);
    }

    private void DrainPending()
    {
        // §10.4 'autoscroll while pinned to tail' is native to ScrollViewer:
        // LogViewer.Append / UpdateLast track the user's pin state and skip
        // the snap-to-bottom when they've scrolled up. The only path still
        // doing direct Items mutation is the Append-above-tail Insert below
        // — that's a deliberate bypass so progress stays at the bottom while
        // regular lines flow in above it.
        while (pending.TryDequeue(out PendingOp op))
        {
            string formatted = FormatLine(op.When, op.Source, op.Message);
            switch (op.Kind)
            {
                case OpKind.Append:
                    if (tailIsInPlace && logViewer.Items.Count > 0)
                    {
                        // Insert above the in-place tail so progress stays at the
                        // bottom while regular lines flow in above it. Items.Insert
                        // bypasses Append's sticky-tail follow, so we re-pin
                        // manually when the user was at the tail.
                        bool wasPinned = logViewer.IsPinnedToTail;
                        logViewer.Items.Insert(
                            logViewer.Items.Count - 1,
                            new LogEntry(formatted, op.SourceColor));
                        if (wasPinned) logViewer.ScrollOffset = logViewer.MaxScrollOffset;
                    }
                    else
                    {
                        logViewer.Append(formatted, op.SourceColor);
                    }
                    break;

                case OpKind.OpenInPlace:
                    logViewer.Append(formatted, op.SourceColor);
                    tailIsInPlace = true;
                    break;

                case OpKind.UpdateInPlace:
                    if (tailIsInPlace) logViewer.UpdateLast(formatted, op.SourceColor);
                    break;

                case OpKind.CompleteInPlace:
                    if (tailIsInPlace) logViewer.UpdateLast(formatted, op.SourceColor);
                    else logViewer.Append(formatted, op.SourceColor);
                    tailIsInPlace = false;
                    break;

                case OpKind.EndInPlace:
                    // Release the gate without changing the tail entry. The
                    // last update frame stays visible as a dangling spinner-
                    // step or progress frame; subsequent Append ops add below.
                    tailIsInPlace = false;
                    break;
            }
        }
    }

    private static string FormatLine(DateTime when, string source, string message)
    {
        // LEASH §10.4: TUI lines carry the same prefix as line mode but in
        // a single source colour. Pad source to match the line-mode width
        // for visual alignment when the user copies the buffer to a file.
        // Leading + trailing space matches the chrome convention (header
        // left/right slots both wrap their text in a single space), so a
        // mouse drag-select across header + logs + footer copies out with
        // a consistent 1-cell gutter on either side.
        string timestamp = when.ToString("HH:mm:ss");
        string padded = source.Length >= 8 ? source : source.PadRight(8);
        return $" {timestamp}  {padded}  {message} ";
    }

    private void EnqueueUpdate(DateTime when, string source, Color sourceColor, string message)
    {
        pending.Enqueue(PendingOp.UpdateInPlace(when, source, sourceColor, message));
        chrome.MarkDirty();
    }

    private void EnqueueComplete(DateTime when, string source, Color sourceColor, string message)
    {
        pending.Enqueue(PendingOp.CompleteInPlace(when, source, sourceColor, message));
        Interlocked.Exchange(ref inPlaceClaimed, 0);
        chrome.MarkDirty();
    }

    private void EnqueueEnd()
    {
        pending.Enqueue(PendingOp.EndInPlace());
        Interlocked.Exchange(ref inPlaceClaimed, 0);
        chrome.MarkDirty();
    }

    private enum OpKind { Append, OpenInPlace, UpdateInPlace, CompleteInPlace, EndInPlace }

    private readonly record struct PendingOp(
        OpKind Kind, DateTime When, string Source, Color SourceColor, string Message)
    {
        public static PendingOp Append(DateTime w, string s, Color c, string m) =>
            new(OpKind.Append, w, s, c, m);
        public static PendingOp OpenInPlace(DateTime w, string s, Color c, string m) =>
            new(OpKind.OpenInPlace, w, s, c, m);
        public static PendingOp UpdateInPlace(DateTime w, string s, Color c, string m) =>
            new(OpKind.UpdateInPlace, w, s, c, m);
        public static PendingOp CompleteInPlace(DateTime w, string s, Color c, string m) =>
            new(OpKind.CompleteInPlace, w, s, c, m);
        public static PendingOp EndInPlace() =>
            new(OpKind.EndInPlace, default, "", default, "");
    }

    /// <summary>
    /// In-place line for TUI mode: each <see cref="Update"/> rewrites the
    /// LogViewer's tail entry; <see cref="Complete"/> rewrites it once
    /// more with the final-state message and releases the in-place gate.
    /// Updates are throttled to 10 Hz at the enqueue side so a tight
    /// download loop does not spam the queue.
    /// </summary>
    private sealed class TuiInPlaceLine(
        HuskyApp owner, DateTime when, string source, Color sourceColor) : ConsoleOutput.IInPlaceLine
    {
        private const long ThrottleMs = 100;

        private long lastUpdateTick = Environment.TickCount64;
        private bool completed;
        private bool disposed;

        public void Update(string message)
        {
            if (completed || disposed) return;
            long now = Environment.TickCount64;
            if (now - lastUpdateTick < ThrottleMs) return;
            lastUpdateTick = now;
            owner.EnqueueUpdate(when, source, sourceColor, message);
        }

        public void UpdateNow(string message)
        {
            if (completed || disposed) return;
            // §10.6: 0% / 100% frame guarantee — bypass the throttle so the
            // final progress frame always lands before the Complete summary
            // replaces it.
            lastUpdateTick = Environment.TickCount64;
            owner.EnqueueUpdate(when, source, sourceColor, message);
        }

        public void Complete(string finalMessage, Color? finalMessageColor = null)
        {
            if (completed) return;
            completed = true;
            // §10.6: the completion line gets a fresh timestamp so the user
            // can read 'started at X, finished at Y' from the buffer.
            owner.EnqueueComplete(
                DateTime.Now, source, finalMessageColor ?? sourceColor, finalMessage);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // If the caller never called Complete, release the gate without
            // changing the tail entry — the last update frame stays visible
            // as a dangling progress/spinner step. Subsequent Appends flow
            // below it as normal lines.
            if (!completed)
            {
                owner.EnqueueEnd();
                completed = true;
            }
        }
    }

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
