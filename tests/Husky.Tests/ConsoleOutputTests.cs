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

    [Theory]
    [InlineData("smoke-app v1.0.0 is up.", "[green]up[/]")]
    [InlineData("status: healthy", "[green]healthy[/]")]
    [InlineData("status: degraded", "[yellow]degraded[/]")]
    [InlineData("status: unhealthy", "[red]unhealthy[/]")]
    [InlineData("app didn't respond. growling.", "[yellow]growling[/]")]
    public void BuildMarkup_highlights_status_words(string message, string expectedFragment)
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "husky", "cyan", message);

        Assert.Contains(expectedFragment, line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMarkup_does_not_highlight_status_words_inside_other_words()
    {
        string line = ConsoleOutput.BuildMarkup(FixedTime, "husky", "cyan", "shutdown-ack received");

        Assert.DoesNotContain("[red]down[/]", line, StringComparison.Ordinal);
    }
}
