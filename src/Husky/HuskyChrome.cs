using Retro.Crt;
using Retro.Crt.Input;
using Retro.Crt.Tui;
using Retro.Crt.Tui.Layout;
using Retro.Crt.Tui.Widgets;

namespace Husky;

/// <summary>
/// Root container for <see cref="HuskyApp"/>. Lays out the three rows
/// described in LEASH §10.4: a 1-row header, a fill-the-rest log
/// viewport, and a 1-row action bar. Holds the chrome-level Esc hotkey
/// that triggers graceful shutdown — buttons and other hotkeys come in
/// later phases.
/// </summary>
internal sealed class HuskyChrome : Container
{
    private readonly Action drainPending;
    private readonly Action onExitRequested;
    private readonly StackPanel body;

    public HuskyChrome(
        string launcherVersion,
        LogViewer log,
        Action drainPending,
        Action onExitRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(drainPending);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        this.drainPending = drainPending;
        this.onExitRequested = onExitRequested;

        var header = new HeaderView(launcherVersion);
        var actionBar = new ActionBarView();

        body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Sizes       = { LayoutSize.Cells(1), LayoutSize.Star(), LayoutSize.Cells(1) },
            Children    = { header, log, actionBar },
        };
        Children.Add(body);
    }

    protected override void ArrangeChildren()
    {
        body.Bounds = Bounds;
    }

    public override void OnDraw(ScreenBuffer screen)
    {
        // Drain queued log entries before the children read LogViewer.Items —
        // this is the only "tick" hook we get on the UI thread without
        // marshalling support in Application.
        drainPending();

        ArrangeChildren();
        for (var i = 0; i < Children.Count; i++)
            Children[i].OnDraw(screen);
    }

    public override bool OnKey(KeyEvent key, Application app)
    {
        if (app.Modal is null && key.Key == Key.Escape)
        {
            onExitRequested();
            return true;
        }
        return base.OnKey(key, app);
    }

    /// <summary>
    /// 1-row banner at the top: launcher branding left-aligned. App name +
    /// version and live health move in here in later phases.
    /// </summary>
    private sealed class HeaderView(string launcherVersion) : View
    {
        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.Black, Color.LightCyan));

            string label = $" husky v{launcherVersion}";
            if (b.Width >= label.Length)
                screen.PutString(b.X, b.Y, label.AsSpan(),
                    Color.Black, Color.LightCyan, CellAttrs.Bold);
        }
    }

    /// <summary>
    /// 1-row strip at the bottom showing the available actions as plain
    /// hint text. Real <see cref="Button"/> widgets and the [c]/[u]/[x]
    /// hotkey wiring land in the next phase; this placeholder keeps the
    /// layout footprint correct and gives users a visible affordance.
    /// </summary>
    private sealed class ActionBarView : View
    {
        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.LightGray, Color.DarkGray));

            const string hint = " [c] copy logs   [u] update now   [x] exit ";
            if (b.Width >= hint.Length)
                screen.PutString(b.X, b.Y, hint.AsSpan(),
                    Color.LightGray, Color.DarkGray);
        }
    }
}
