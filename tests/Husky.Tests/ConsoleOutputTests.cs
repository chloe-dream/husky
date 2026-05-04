using Husky;
using Retro.Crt;

namespace Husky.Tests;

// Gate state in ConsoleOutput is process-global. Any test class that may
// hold the widget (directly or via UpdateExtractor / UpdateFlow / shutdown
// paths) shares this collection so xUnit runs them sequentially.
[CollectionDefinition(Name)]
public sealed class ConsoleOutputGateCollection { public const string Name = "ConsoleOutputGate"; }

[Collection(ConsoleOutputGateCollection.Name)]
public sealed class ConsoleOutputTests
{
    private static readonly DateTime FixedTime = new(2026, 5, 1, 14, 55, 42);

    [Fact]
    public void BuildLine_uses_HH_mm_ss_timestamp()
    {
        var segs = ConsoleOutput.BuildLine(FixedTime, "husky", Color.LightCyan, "started");

        Assert.Equal("14:55:42", segs[0].Text);
        Assert.Equal(Color.DarkGray, segs[0].Color);
    }

    [Fact]
    public void BuildLine_right_pads_source_to_eight_characters()
    {
        var segs = ConsoleOutput.BuildLine(FixedTime, "app", Color.LightGreen, "hi");

        var sourceSeg = SegmentWithText(segs, "app     ");
        Assert.Equal(Color.LightGreen, sourceSeg.Color);
    }

    [Fact]
    public void BuildLine_does_not_truncate_sources_longer_than_eight_characters()
    {
        var segs = ConsoleOutput.BuildLine(FixedTime, "longersrc", Color.Yellow, "hi");

        var sourceSeg = SegmentWithText(segs, "longersrc");
        Assert.Equal(Color.Yellow, sourceSeg.Color);
    }

    [Fact]
    public void BuildLine_passes_through_brackets_unchanged()
    {
        // Retro.Crt has no markup language, so the bracket-escape dance Spectre
        // required is gone — the message should reach the renderer verbatim.
        var segs = ConsoleOutput.BuildLine(FixedTime, "app", Color.LightGreen, "[red]boom[/]");

        var combined = string.Concat(segs.Select(s => s.Text));
        Assert.EndsWith("[red]boom[/]", combined, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("smoke-app v1.0.0 is up.",            "up",        Standard16Index.LightGreen)]
    [InlineData("status: healthy",                    "healthy",   Standard16Index.LightGreen)]
    [InlineData("status: degraded",                   "degraded",  Standard16Index.Yellow)]
    [InlineData("status: unhealthy",                  "unhealthy", Standard16Index.LightRed)]
    [InlineData("app didn't respond. growling.",      "growling",  Standard16Index.Yellow)]
    public void BuildLine_highlights_status_words(string message, string word, Standard16Index expected)
    {
        var segs = ConsoleOutput.BuildLine(FixedTime, "husky", Color.LightCyan, message);

        var seg = SegmentWithText(segs, word);
        Assert.NotNull(seg.Color);
        Assert.Equal((byte)expected, seg.Color!.Value.Index);
        Assert.Equal(ColorMode.Standard16, seg.Color.Value.Mode);
    }

    [Fact]
    public void BuildLine_does_not_highlight_status_words_inside_other_words()
    {
        var segs = ConsoleOutput.BuildLine(FixedTime, "husky", Color.LightCyan, "shutdown-ack received");

        // No segment should be the bare word "down" with a color attached.
        var rogue = segs.FirstOrDefault(s => s.Text == "down" && s.Color is not null);
        Assert.Equal(default, rogue);
    }

    [Fact]
    public void LiveWidget_queues_app_lines_until_dispose()
    {
        using var sink = new StringWriter();
        using (Crt.WithSink(sink))
        {
            var widget = ConsoleOutput.BeginLiveWidget();

            ConsoleOutput.AppOut("first");
            ConsoleOutput.AppOut("second");

            // While the widget is active, nothing should have reached the sink.
            Assert.Equal(string.Empty, sink.ToString());

            widget.Dispose();

            // After dispose, both queued lines flush in arrival order.
            var output = sink.ToString();
            var firstIdx = output.IndexOf("first", StringComparison.Ordinal);
            var secondIdx = output.IndexOf("second", StringComparison.Ordinal);
            Assert.True(firstIdx >= 0 && secondIdx > firstIdx,
                $"Expected 'first' before 'second' in flush; got: {output}");
        }
    }

    [Fact]
    public void LiveWidget_overflow_drops_oldest_and_logs_elided_marker()
    {
        const int Cap = ConsoleOutput.LiveWidgetQueueCap;
        const int Overflow = 5;

        using var sink = new StringWriter();
        using (Crt.WithSink(sink))
        {
            var widget = ConsoleOutput.BeginLiveWidget();

            for (var i = 0; i < Cap + Overflow; i++)
                ConsoleOutput.AppOut($"line-{i}");

            widget.Dispose();

            var output = sink.ToString();

            // The first 5 lines should have been dropped; line-5 .. line-(Cap+4) survive.
            for (var i = 0; i < Overflow; i++)
                Assert.DoesNotContain($"line-{i} ", output, StringComparison.Ordinal);
            Assert.Contains($"line-{Overflow}", output, StringComparison.Ordinal);
            Assert.Contains($"line-{Cap + Overflow - 1}", output, StringComparison.Ordinal);

            Assert.Contains($"… {Overflow} app line(s) elided", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Husky_force_flag_bypasses_active_widget_queue()
    {
        using var sink = new StringWriter();
        using (Crt.WithSink(sink))
        {
            var widget = ConsoleOutput.BeginLiveWidget();

            ConsoleOutput.Husky("forced-line", force: true);

            // Force lines write through immediately, without waiting for dispose.
            Assert.Contains("forced-line", sink.ToString(), StringComparison.Ordinal);

            widget.Dispose();
        }
    }

    [Fact]
    public void BeginLiveWidget_throws_when_one_is_already_active()
    {
        var first = ConsoleOutput.BeginLiveWidget();
        try
        {
            Assert.Throws<InvalidOperationException>(() => ConsoleOutput.BeginLiveWidget());
        }
        finally
        {
            first.Dispose();
        }
    }

    private static ConsoleOutput.LineSegment SegmentWithText(
        IReadOnlyList<ConsoleOutput.LineSegment> segs, string text)
    {
        for (var i = 0; i < segs.Count; i++)
            if (segs[i].Text == text) return segs[i];
        throw new Xunit.Sdk.XunitException($"No segment with text '{text}'.");
    }

    public enum Standard16Index : byte
    {
        DarkGray    = 8,
        LightRed    = 12,
        LightGreen  = 10,
        LightCyan   = 11,
        Yellow      = 14,
    }
}
