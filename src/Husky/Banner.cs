using Spectre.Console;

namespace Husky;

internal static class Banner
{
    private const string Logo =
"""
[aqua]                                      ▆▇▂                   ▁▇▇
                                    ▆▇██▇▇▂               ▂▇▇██▇▇▁
                                    ██▃▂▇█▇▇▃           ▂▇▇██▂▂██▁
                                 ▁▆▆▃▂▁ ▂▂▇█▄           ▂█▇▂▂  ▂▂▆▆▂
                                  ██▁     ▆█▄ ▄▆▆▆▆▆▆▆▅ ▂█▇      ██▂
                                  ██▁   ▁ ▂▃▅▆█████████▆▅▃▂      ██▂
                                  ██▁   ▁ ▄▆▇████████████▆▅      ██▂
                                  ██▁   ▅▆█████████████████▆▆    ██▂
                                  ██▁ ▅▅████████▆▃▃▃▅████████▅▅  ██▂
                                  ██▅▅██████████▄   ▃██████████▅▅██▂
                                  ██████████▅▄▆█▄   ▃█▇▄▄██████████▂
                                  ████████▅▄▂ ▃▄▂   ▂▄▃ ▁▄▄████████▂
                                ▄▄██████▅▄▁               ▁▄▄██████▅▅▁
                                ██████▅▅▄▄▁ ▃▄▂       ▁▄▃ ▁▄▄▅▅██████▂
                                ██████▁ ▄▅▄▄▇█▆▄▂   ▁▄▅█▇▃▄▅▅  ██████▂
                                ████▆▅▁   ▄▅▅▅▅▅▃   ▂▅▅▅▅▅▅    ▅▅████▂
                               ▁▆▆██▁                            ██▆▆▂
                                  ██▁                            ██▂
                                  ██▁         ▁▂▂▂▂▂▂▂▂          ██▂
                                  ▇▇▃▃▁       ▅███████▆       ▁▂▃▆▇▂
                                    ▆▇▃▂▂▂▁   ▄▇█████▇▅   ▁▂▂▂▂▇▇▁
                                      ▆▇▇▇▄▂▁   ▄███▅   ▁▂▃▇▇▇▇
                                          ▆█▄▂▁ ▃▇▇▇▅ ▁▂▃▇▇
                                            ▅█▄▂▂▂▂▂▂▂▃█▆
                                              ▅███████▆[/]
""";

    public static void Render(string version)
    {
        AnsiConsole.MarkupLine(Logo);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold cyan]Husky[/] [dim]v{Markup.Escape(version)}[/]");
        AnsiConsole.MarkupLine("  [dim]your loyal app launcher[/]");
        AnsiConsole.WriteLine();
    }
}
