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
    private readonly HeaderView header;

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

        header = new HeaderView(launcherVersion);
        var actionBar = new ActionBarView();

        body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Sizes       = { LayoutSize.Cells(1), LayoutSize.Star(), LayoutSize.Cells(1) },
            Children    = { header, log, actionBar },
        };
        Children.Add(body);
    }

    /// <summary>Update the header's app-info slot. Safe from any thread.</summary>
    public void SetAppInfo(string? appName, string? appVersion)
    {
        header.SetAppInfo(appName, appVersion);
        MarkDirty();
    }

    /// <summary>Update the header's health slot. Safe from any thread.</summary>
    public void SetHealth(string? status)
    {
        header.SetHealth(status);
        MarkDirty();
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
    /// 1-row banner at the top: launcher branding left-aligned, app
    /// name+version centered (or '(starting…)' before the handshake),
    /// health status right-aligned in its semantic colour. State setters
    /// are thread-safe via a tiny lock; the UI thread snapshots under the
    /// same lock during <see cref="OnDraw"/>.
    /// </summary>
    private sealed class HeaderView(string launcherVersion) : View
    {
        private readonly object stateLock = new();
        private string? appName;
        private string? appVersion;
        private string? health;

        public void SetAppInfo(string? name, string? version)
        {
            lock (stateLock) { appName = name; appVersion = version; }
            MarkDirty();
        }

        public void SetHealth(string? status)
        {
            lock (stateLock) { health = status; }
            MarkDirty();
        }

        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            string? localName, localVer, localHealth;
            lock (stateLock)
            {
                localName = appName;
                localVer = appVersion;
                localHealth = health;
            }

            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.Black, Color.LightCyan));

            // Left: launcher branding.
            string left = $" husky v{launcherVersion}";
            int leftLen = Math.Min(left.Length, b.Width);
            screen.PutString(b.X, b.Y, left.AsSpan(0, leftLen),
                Color.Black, Color.LightCyan, CellAttrs.Bold);

            // Right: health status, or a status hint while no app is attached.
            (string text, Color color) right = ResolveRightSlot(localName, localHealth);
            int rightLen = Math.Min(right.text.Length, b.Width - leftLen);
            int rightX = b.X + b.Width - rightLen;
            if (rightLen > 0 && rightX >= b.X + leftLen)
            {
                screen.PutString(rightX, b.Y, right.text.AsSpan(0, rightLen),
                    right.color, Color.LightCyan, CellAttrs.Bold);
            }

            // Middle: app name + version, centered between the left and right
            // slots. Drop entirely when the slot can't fit the full text — a
            // truncated app-name is worse than no name.
            if (localName is not null)
            {
                string mid = localVer is null ? localName : $"{localName} v{localVer}";
                int leftEnd = b.X + leftLen;
                int rightStart = rightLen > 0 ? rightX : b.X + b.Width;
                int slotStart = leftEnd + 1;
                int slotEnd = rightStart - 1;
                int slotWidth = slotEnd - slotStart;
                if (slotWidth >= mid.Length)
                {
                    int midX = slotStart + (slotWidth - mid.Length) / 2;
                    screen.PutString(midX, b.Y, mid.AsSpan(),
                        Color.Black, Color.LightCyan, CellAttrs.Bold);
                }
            }
        }

        private static (string text, Color color) ResolveRightSlot(string? name, string? health)
        {
            if (health is not null)
                return ($" {health} ", HealthColour(health));
            if (name is null)
                return (" (starting…) ", Color.DarkGray);
            return (string.Empty, Color.Black);
        }

        private static Color HealthColour(string status) => status switch
        {
            "healthy"   => Color.LightGreen,
            "degraded"  => Color.Yellow,
            "unhealthy" => Color.LightRed,
            _           => Color.Black,
        };
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
