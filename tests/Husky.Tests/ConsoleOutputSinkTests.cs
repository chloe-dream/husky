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
    public void BeginInPlaceHusky_routes_initial_message_and_updates_to_the_sink()
    {
        var fake = new RecordingSink();
        try
        {
            ConsoleOutput.SetSink(fake);

            using ConsoleOutput.IInPlaceLine line = ConsoleOutput.BeginInPlaceHusky("fetching… 0%");
            line.Update("fetching… 50%");
            line.Complete("fetched 6.8 MB in 4.2 s.", Color.LightGreen);

            RecordedInPlace entry = Assert.Single(fake.InPlaceLines);
            Assert.Equal("husky", entry.Source);
            Assert.Equal(Color.LightCyan, entry.SourceColor);
            Assert.Equal("fetching… 0%", entry.InitialMessage);
            Assert.Equal(["fetching… 50%"], entry.Updates);
            Assert.Equal("fetched 6.8 MB in 4.2 s.", entry.CompletedMessage);
            Assert.Equal(Color.LightGreen, entry.CompletedColor);
        }
        finally
        {
            ConsoleOutput.ResetSink();
        }
    }

    [Fact]
    public void SetAppInfo_and_SetHealth_route_to_the_active_sink()
    {
        var fake = new RecordingSink();
        try
        {
            ConsoleOutput.SetSink(fake);

            ConsoleOutput.SetAppInfo("umbrella-bot", "1.4.2");
            ConsoleOutput.SetHealth("healthy");
            ConsoleOutput.SetHealth("degraded");
            ConsoleOutput.SetAppInfo(null, null);

            Assert.Equal(2, fake.AppInfoUpdates);
            Assert.Equal(2, fake.HealthUpdates);
            Assert.Null(fake.AppName);
            Assert.Null(fake.AppVersion);
            Assert.Equal("degraded", fake.Health);
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
        public List<RecordedInPlace> InPlaceLines { get; } = [];

        public void Append(
            DateTime when, string source, Color sourceColor, string message,
            Color? messageColor, bool force) =>
            Lines.Add(new RecordedLine(source, sourceColor, message, force));

        public IDisposable BeginLiveWidget()
        {
            LiveWidgetScopesOpened++;
            return new NullScope();
        }

        public ConsoleOutput.IInPlaceLine BeginInPlaceLine(
            string source, Color sourceColor, string initialMessage)
        {
            var entry = new RecordedInPlace(source, sourceColor, initialMessage);
            InPlaceLines.Add(entry);
            return entry;
        }

        public string? AppName { get; private set; }
        public string? AppVersion { get; private set; }
        public string? Health { get; private set; }
        public int AppInfoUpdates { get; private set; }
        public int HealthUpdates { get; private set; }

        public void SetAppInfo(string? appName, string? appVersion)
        {
            AppName = appName;
            AppVersion = appVersion;
            AppInfoUpdates++;
        }

        public void SetHealth(string? status)
        {
            Health = status;
            HealthUpdates++;
        }

        private sealed class NullScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    public sealed class RecordedInPlace(string source, Color sourceColor, string initialMessage)
        : ConsoleOutput.IInPlaceLine
    {
        public string Source { get; } = source;
        public Color SourceColor { get; } = sourceColor;
        public string InitialMessage { get; } = initialMessage;
        public List<string> Updates { get; } = [];
        public string? CompletedMessage { get; private set; }
        public Color? CompletedColor { get; private set; }
        public bool Disposed { get; private set; }

        public void Update(string message) => Updates.Add(message);

        public void Complete(string finalMessage, Color? finalMessageColor = null)
        {
            CompletedMessage = finalMessage;
            CompletedColor = finalMessageColor;
        }

        public void Dispose() => Disposed = true;
    }

    public readonly record struct RecordedLine(string Source, Color SourceColor, string Message, bool Force);
}
