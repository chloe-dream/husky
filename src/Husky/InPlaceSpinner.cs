using Retro.Crt;

namespace Husky;

/// <summary>
/// Wraps a <see cref="ConsoleOutput.IInPlaceLine"/> with a periodic timer
/// that cycles through a set of glyphs, so an indeterminate operation
/// (sniff-poll, extract, graceful-shutdown wait) renders as one updating
/// husky-tagged log line that visibly ticks while the operation runs.
/// In line mode the timer's <see cref="ConsoleOutput.IInPlaceLine.Update"/>
/// calls are no-ops (LEASH §10.3 auto-degrade), so only the start label
/// and the final-state line surface.
/// </summary>
internal sealed class InPlaceSpinner : IDisposable
{
    private static readonly char[] Frames =
        ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    private const int FrameIntervalMs = 100;

    private readonly ConsoleOutput.IInPlaceLine line;
    private readonly Timer timer;
    private string label;
    private int frameIndex;
    private bool completed;
    private bool disposed;

    public InPlaceSpinner(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        this.label = label;
        // Open with frame 0 so the first paint already shows a glyph.
        line = ConsoleOutput.BeginInPlaceHusky($"{Frames[0]} {label}");
        timer = new Timer(
            static state => ((InPlaceSpinner)state!).OnTick(),
            state: this,
            dueTime: FrameIntervalMs,
            period: FrameIntervalMs);
    }

    /// <summary>
    /// Replace the label text. The next animation tick picks it up; the
    /// glyph keeps ticking. Call this for intermediate state changes
    /// (e.g., 'no shutdown-ack — waiting anyway' during graceful shutdown).
    /// </summary>
    public void UpdateLabel(string newLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newLabel);
        label = newLabel;
    }

    /// <summary>
    /// Stop the animation and replace the in-place line with a final-state
    /// message. The completion line gets a fresh timestamp (LEASH §10.6).
    /// </summary>
    public void Complete(string finalMessage, Color? finalColor = null)
    {
        if (completed) return;
        completed = true;
        timer.Dispose();
        line.Complete(finalMessage, finalColor);
    }

    /// <summary>
    /// Stop the animation and release the in-place gate without writing a
    /// final line. The last animation frame stays visible. Used when the
    /// caller does not own the result announcement (e.g., the boot poll
    /// in <c>Program.cs</c>, where <c>LauncherRuntime</c> emits the
    /// outcome line later).
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!completed)
        {
            timer.Dispose();
            line.Dispose();
            completed = true;
        }
    }

    private void OnTick()
    {
        if (completed) return;
        int next = Interlocked.Increment(ref frameIndex);
        char frame = Frames[next % Frames.Length];
        // Snapshot the label reference once — string assignments are atomic
        // on .NET so a torn read can't happen, but a label swap mid-format
        // would still be harmless.
        string snapshot = label;
        line.Update($"{frame} {snapshot}");
    }
}
