using System.Globalization;
using Retro.Crt;

namespace Husky;

/// <summary>
/// <see cref="IDownloadProgress"/> implementation that folds a download
/// into a single self-updating <c>husky</c>-tagged log line via
/// <see cref="ConsoleOutput.BeginInPlaceHusky"/> (LEASH §10.6). The
/// active sink picks the rendering: TUI mode rewrites the LogViewer's
/// tail entry on every <see cref="OnAdvanced"/>; line mode degrades to
/// a start line and a final summary line (LEASH §10.3) — no cursor
/// magic, pipe-friendly. Long-lived: one instance per launcher process,
/// each download reuses the sink. Disposing the sink itself releases
/// any half-started in-place line (the safety net for downloads that
/// throw before <see cref="OnFinished"/>).
/// </summary>
internal sealed class ProgressBarDownloadSink : IDownloadProgress, IDisposable
{
    private const int BarWidth = 30;

    private ConsoleOutput.IInPlaceLine? line;
    private long totalKnown;

    /// <inheritdoc />
    public void OnStarted(long? totalBytes)
    {
        // Defensive: a previous download that crashed before OnFinished may
        // have left state behind. Reset before opening a new line.
        ResetUnsafe();

        totalKnown = totalBytes is > 0 ? totalBytes.Value : 0;

        line = ConsoleOutput.BeginInPlaceHusky(
            BuildBarMessage(received: 0, total: totalKnown));
    }

    /// <inheritdoc />
    public void OnAdvanced(long bytesReceived) =>
        line?.Update(BuildBarMessage(bytesReceived, totalKnown));

    /// <inheritdoc />
    public void OnFinished(long bytesReceived, TimeSpan duration)
    {
        if (line is null) return;

        line.Complete(
            $"fetched {HumanBytes.Format(bytesReceived)} in {FormatDuration(duration)}.",
            Color.LightGreen);
        line.Dispose();
        line = null;
    }

    /// <summary>
    /// Releases any active in-place line. Safe to call multiple times and
    /// at any point in the sink's lifecycle — the safety net for downloads
    /// that throw between <see cref="OnStarted"/> and <see cref="OnFinished"/>.
    /// </summary>
    public void Dispose() => ResetUnsafe();

    private void ResetUnsafe()
    {
        line?.Dispose();
        line = null;
    }

    private static string BuildBarMessage(long received, long total)
    {
        if (total <= 0)
        {
            // Unknown total: show received bytes only with an indeterminate
            // marker. Still useful to confirm the stream is moving.
            return $"fetching … {HumanBytes.Format(received)}";
        }

        double fraction = Math.Clamp((double)received / total, 0.0, 1.0);
        int filledCells = (int)Math.Round(fraction * BarWidth);
        int emptyCells = BarWidth - filledCells;
        int percent = (int)Math.Round(fraction * 100.0);

        return
            $"fetching {new string('█', filledCells)}{new string('░', emptyCells)} {percent,3}%  " +
            $"{HumanBytes.Format(received)} / {HumanBytes.Format(total)}";
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalSeconds < 1
            ? $"{d.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms"
            : $"{d.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
