using Husky;
using Retro.Crt;

namespace Husky.Tests;

[Collection(ConsoleOutputGateCollection.Name)]
public sealed class ConsoleOutputSinkTests
{
    [Fact]
    public void SetSink_routes_facade_calls_to_the_active_sink()
    {
        var fake = new RecordingSink();
        try
        {
            ConsoleOutput.SetSink(fake);

            ConsoleOutput.Husky("hello");
            ConsoleOutput.AppOut("from app");
            ConsoleOutput.AppErr("error from app");
            ConsoleOutput.Pipe("pipe trace");

            Assert.Collection(fake.Lines,
                e => { Assert.Equal("husky", e.Source); Assert.Equal(Color.LightCyan, e.SourceColor); Assert.Equal("hello", e.Message); },
                e => { Assert.Equal("app", e.Source);   Assert.Equal(Color.LightGreen, e.SourceColor); Assert.Equal("from app", e.Message); },
                e => { Assert.Equal("app", e.Source);   Assert.Equal(Color.LightRed, e.SourceColor);   Assert.Equal("error from app", e.Message); },
                e => { Assert.Equal("pipe", e.Source);  Assert.Equal(Color.DarkGray, e.SourceColor);   Assert.Equal("pipe trace", e.Message); });
        }
        finally
        {
            ConsoleOutput.ResetSink();
        }
    }

    [Fact]
    public void BeginLiveWidget_delegates_to_the_active_sink()
    {
        var fake = new RecordingSink();
        try
        {
            ConsoleOutput.SetSink(fake);

            using IDisposable scope = ConsoleOutput.BeginLiveWidget();

            Assert.Equal(1, fake.LiveWidgetScopesOpened);
        }
        finally
        {
            ConsoleOutput.ResetSink();
        }
    }

    [Fact]
    public void ResetSink_restores_the_default_Crt_sink()
    {
        var fake = new RecordingSink();

        ConsoleOutput.SetSink(fake);
        ConsoleOutput.ResetSink();

        // The default sink doesn't record into our fake — confirm by emitting
        // and checking the fake stayed empty after the reset.
        ConsoleOutput.Husky("after reset");
        Assert.Empty(fake.Lines);
    }

    private sealed class RecordingSink : ConsoleOutput.IConsoleSink
    {
        public List<RecordedLine> Lines { get; } = [];
        public int LiveWidgetScopesOpened { get; private set; }

        public void Append(
            DateTime when, string source, Color sourceColor, string message,
            Color? messageColor, bool force) =>
            Lines.Add(new RecordedLine(source, sourceColor, message, force));

        public IDisposable BeginLiveWidget()
        {
            LiveWidgetScopesOpened++;
            return new NullScope();
        }

        private sealed class NullScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    public readonly record struct RecordedLine(string Source, Color SourceColor, string Message, bool Force);
}
