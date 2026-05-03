using System.Text.RegularExpressions;
using Retro.Crt;

namespace Husky;

internal static partial class ConsoleOutput
{
    private const int SourceWidth = 8;

    public static void Husky(string message) => Render("husky", Color.LightCyan,  message);
    public static void AppOut(string message) => Render("app",   Color.LightGreen, message);
    public static void AppErr(string message) => Render("app",   Color.LightRed,   message);
    public static void Pipe(string message)   => Render("pipe",  Color.DarkGray,   message);

    private static void Render(string source, Color sourceColor, string message)
    {
        var line = BuildLine(DateTime.Now, source, sourceColor, message);
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
}
