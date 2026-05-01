using Husky;

namespace Husky.Tests;

public sealed class ConsoleOutputTests
{
    private static readonly DateTime FixedTime = new(2026, 5, 1, 14, 55, 42);

    [Fact]
    public void BuildMarkup_uses_HH_mm_ss_timestamp()
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "husky", "cyan", "started");

        Assert.StartsWith("[grey]14:55:42[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkup_right_pads_source_to_eight_characters()
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "app", "green", "hi");

        Assert.Contains("[green]app     [/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkup_does_not_truncate_sources_longer_than_eight_characters()
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "longersrc", "yellow", "hi");

        Assert.Contains("[yellow]longersrc[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkup_escapes_message_brackets_so_they_are_not_treated_as_markup()
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "app", "green", "[red]boom[/]");

        Assert.EndsWith("[[red]]boom[[/]]", line, StringComparison.Ordinal);
    }
}
