using Spectre.Console;

namespace Husky;

internal static class Banner
{
    // Final ASCII art is the designer's choice (LEASH §10.2). The placeholder
    // below keeps the layout stable; step 8 (console rendering) replaces it.
    private const string LogoPlaceholder = "  <husky-ascii-art>";

    public static void Render(string version)
    {
        AnsiConsole.MarkupLine($"[aqua]{Markup.Escape(LogoPlaceholder)}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold cyan]Husky[/] [dim]v{Markup.Escape(version)}[/]");
        AnsiConsole.MarkupLine("  [dim]your loyal app launcher[/]");
        AnsiConsole.WriteLine();
    }
}
