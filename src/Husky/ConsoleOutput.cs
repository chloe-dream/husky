using System.Text.RegularExpressions;
using Spectre.Console;

namespace Husky;

internal static partial class ConsoleOutput
{
    private const int SourceWidth = 8;

    public static void Husky(string message) => Render("husky", "cyan", message);
    public static void AppOut(string message) => Render("app", "green", message);
    public static void AppErr(string message) => Render("app", "red", message);
    public static void Pipe(string message) => Render("pipe", "grey50", message);

    private static void Render(string source, string color, string message) =>
        AnsiConsole.MarkupLine(BuildMarkup(DateTime.Now, source, color, message));

    internal static string BuildMarkup(DateTime when, string source, string color, string message)
    {
        string timestamp = when.ToString("HH:mm:ss");
        string padded = source.Length >= SourceWidth ? source : source.PadRight(SourceWidth);
        string body = ApplyStatusHighlights(Markup.Escape(message));
        return $"[grey]{timestamp}[/]  [{color}]{padded}[/]  {body}";
    }

    /// <summary>
    /// Re-colours the well-known status verbs (LEASH §10.3) inside an
    /// already-escaped message body. The match runs on word boundaries to
    /// avoid colouring substrings of identifiers like "shutdown-ack".
    /// </summary>
    private static string ApplyStatusHighlights(string escaped) =>
        StatusWordRegex().Replace(escaped, match => match.Value switch
        {
            "up" => "[green]up[/]",
            "down" => "[red]down[/]",
            "healthy" => "[green]healthy[/]",
            "degraded" => "[yellow]degraded[/]",
            "unhealthy" => "[red]unhealthy[/]",
            "growling" => "[yellow]growling[/]",
            _ => match.Value,
        });

    [GeneratedRegex(@"\b(up|down|healthy|degraded|unhealthy|growling)\b")]
    private static partial Regex StatusWordRegex();
}
