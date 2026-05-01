using Spectre.Console;

namespace Husky;

internal static class ConsoleOutput
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
        return $"[grey]{timestamp}[/]  [{color}]{padded}[/]  {Markup.Escape(message)}";
    }
}
