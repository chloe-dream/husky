using System.Globalization;
using Retro.Crt;

namespace Husky;

/// <summary>
/// <see cref="IDownloadProgress"/> implementation that drives a
/// <see cref="ProgressBar"/> inside a <see cref="ConsoleOutput.BeginLiveWidget"/>
/// scope (LEASH §10.5 / §10.7). Long-lived: one instance per launcher
/// process; each download reuses the sink — fresh widget + bar per
/// <see cref="OnStarted"/>, torn down in <see cref="OnFinished"/>. After
/// finish, a normal-format husky log line carries the byte count and
/// duration. Disposing the sink itself releases any half-started
/// widget+bar (the safety net for downloads that throw before
/// <see cref="OnFinished"/>).
/// </summary>
internal sealed class ProgressBarDownloadSink : IDownloadProgress, IDisposable
{
    private const int BarWidth = 30;

    private IDisposable? widgetScope;
    private ProgressBar? bar;
    private long totalKnown;

    /// <inheritdoc />
    public void OnStarted(long? totalBytes)
    {
        // Defensive: a previous download that crashed before OnFinished may
        // have left state behind. Reset before opening a new scope.
        ResetUnsafe();

        widgetScope = ConsoleOutput.BeginLiveWidget();

        // ProgressBar needs a positive total; when Content-Length is missing
        // we fall back to a one-cell bar that simply pings to "100%" once
        // the stream completes. The summary log line carries the real size.
        long total = totalBytes is > 0 ? totalBytes.Value : 1;
        totalKnown = totalBytes ?? 0;

        bar = ProgressBar.Start(
            total: total,
            width: BarWidth,
            label: "fetching",
            color: Color.LightCyan);
    }

    /// <inheritdoc />
    public void OnAdvanced(long bytesReceived) => bar?.Set(bytesReceived);

    /// <inheritdoc />
    public void OnFinished(long bytesReceived, TimeSpan duration)
    {
        bar?.Set(totalKnown > 0 ? totalKnown : 1);
        bar?.Dispose();
        bar = null;

        widgetScope?.Dispose();
        widgetScope = null;

        ConsoleOutput.Husky(
            $"fetched {HumanBytes.Format(bytesReceived)} in {FormatDuration(duration)}.");
    }

    /// <summary>
    /// Releases any active bar and live-widget scope. Safe to call
    /// multiple times and at any point in the sink's lifecycle —
    /// the safety net for downloads that throw between
    /// <see cref="OnStarted"/> and <see cref="OnFinished"/>.
    /// </summary>
    public void Dispose() => ResetUnsafe();

    private void ResetUnsafe()
    {
        bar?.Dispose();
        bar = null;
        widgetScope?.Dispose();
        widgetScope = null;
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalSeconds < 1
            ? $"{d.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms"
            : $"{d.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
}
