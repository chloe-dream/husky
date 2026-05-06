using Retro.Crt;
using Retro.Crt.Input;
using Retro.Crt.Tui;
using Retro.Crt.Tui.Layout;
using Retro.Crt.Tui.Widgets;

namespace Husky;

/// <summary>
/// Root container for <see cref="HuskyApp"/>. Lays out the three rows
/// described in LEASH §10.4: a 1-row header, a fill-the-rest log
/// viewport, and a 1-row action bar with three real focusable buttons.
/// Owns the chrome-level hotkeys (c/u/x/Esc/Ctrl+C) that shadow the
/// buttons so users can drive the TUI from the keyboard alone.
/// </summary>
internal sealed class HuskyChrome : Container
{
    private readonly Action drainPending;
    private readonly Action onCopyRequested;
    private readonly Action onUpdateRequested;
    private readonly Action onExitRequested;
    private readonly StackPanel body;
    private readonly HeaderView header;

    public HuskyChrome(
        string launcherVersion,
        LogViewer log,
        Action drainPending,
        Action onCopyRequested,
        Action onUpdateRequested,
        Action onExitRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(drainPending);
        ArgumentNullException.ThrowIfNull(onCopyRequested);
        ArgumentNullException.ThrowIfNull(onUpdateRequested);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        this.drainPending = drainPending;
        this.onCopyRequested = onCopyRequested;
        this.onUpdateRequested = onUpdateRequested;
        this.onExitRequested = onExitRequested;

        header = new HeaderView(launcherVersion);
        var actionBar = new ActionBar(onCopyRequested, onUpdateRequested, onExitRequested);

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
        if (app.Modal is not null) return base.OnKey(key, app);

        // Esc / x / Ctrl+C → graceful exit. Two press of the latter could
        // escalate to hard kill in the future; for now any of them just
        // requests a graceful shutdown.
        if (key.Key == Key.Escape) { onExitRequested(); return true; }
        if (key.Key == Key.Glyph)
        {
            char g = key.Glyph;
            // Ctrl+C maps to a glyph 'c' with the Ctrl modifier on most
            // terminals; treat it as the exit hotkey to match shell habits.
            if (g is 'c' or 'C' && (key.Modifiers & KeyModifiers.Ctrl) != 0)
            {
                onExitRequested();
                return true;
            }
            // Plain letters trigger the corresponding action — no Ctrl/Alt.
            if ((key.Modifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
            {
                switch (g)
                {
                    case 'c' or 'C': onCopyRequested();   return true;
                    case 'u' or 'U': onUpdateRequested(); return true;
                    case 'x' or 'X': onExitRequested();   return true;
                }
            }
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

            // Header shares the action-bar's DarkGray background so the bars
            // bookend the LogViewer with consistent chrome. Cyan stays as an
            // accent on the launcher branding rather than as a full bar
            // colour — pastel status colours (LightGreen / Yellow / LightRed)
            // read poorly on a LightCyan fill.
            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.LightGray, Color.DarkGray));

            // Left: launcher branding in the husky accent colour.
            string left = $" husky v{launcherVersion}";
            int leftLen = Math.Min(left.Length, b.Width);
            screen.PutString(b.X, b.Y, left.AsSpan(0, leftLen),
                Color.LightCyan, Color.DarkGray, CellAttrs.Bold);

            // Right: health status, or a status hint while no app is attached.
            (string text, Color color) right = ResolveRightSlot(localName, localHealth);
            int rightLen = Math.Min(right.text.Length, b.Width - leftLen);
            int rightX = b.X + b.Width - rightLen;
            if (rightLen > 0 && rightX >= b.X + leftLen)
            {
                screen.PutString(rightX, b.Y, right.text.AsSpan(0, rightLen),
                    right.color, Color.DarkGray, CellAttrs.Bold);
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
                        Color.White, Color.DarkGray, CellAttrs.Bold);
                }
            }
        }

        private static (string text, Color color) ResolveRightSlot(string? name, string? health)
        {
            if (health is not null)
                return ($" {health} ", HealthColour(health));
            if (name is null)
                return (" (starting…) ", Color.LightGray);
            return (string.Empty, Color.LightGray);
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
    /// 1-row strip at the bottom showing the three commands as
    /// dot-separated hotkey hints (<c>c copy logs · u update now · x exit</c>).
    /// The hotkey letter is in the launcher's accent colour and bold; the
    /// label is plain on the bar's dark background; the middle-dot
    /// separator picks up the label colour. Activation goes through
    /// <see cref="HuskyChrome.OnKey"/>'s global hotkeys — there are no
    /// focusable button widgets here, since Tab cycling adds nothing
    /// when every command already has a single-letter shortcut.
    /// </summary>
    private sealed class ActionBar(Action onCopy, Action onUpdate, Action onExit) : View
    {
        // Suppress 'declared but unused' — these stay assigned in case a
        // future commit adds OnMouse hit-test handling per region.
        private readonly Action onCopy = onCopy;
        private readonly Action onUpdate = onUpdate;
        private readonly Action onExit = onExit;

        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.LightGray, Color.DarkGray));

            int x = b.X + 1;
            int max = b.X + b.Width;
            x = DrawCommand(screen, x, b.Y, max, "c", "copy logs");
            x = DrawSeparator(screen, x, b.Y, max);
            x = DrawCommand(screen, x, b.Y, max, "u", "update now");
            x = DrawSeparator(screen, x, b.Y, max);
            DrawCommand(screen, x, b.Y, max, "x", "exit");

            // Reference the action fields so the analyser doesn't flag them
            // as unused before the OnMouse hit-test work lands.
            _ = onCopy; _ = onUpdate; _ = onExit;
        }

        private static int DrawCommand(
            ScreenBuffer screen, int x, int y, int max, string hotkey, string label)
        {
            int hkLen = Math.Min(hotkey.Length, max - x);
            if (hkLen > 0)
                screen.PutString(x, y, hotkey.AsSpan(0, hkLen),
                    Color.LightCyan, Color.DarkGray, CellAttrs.Bold);
            x += hkLen;

            if (x < max)
            {
                screen.PutString(x, y, " ".AsSpan(), Color.LightGray, Color.DarkGray);
                x += 1;
            }

            int lblLen = Math.Min(label.Length, max - x);
            if (lblLen > 0)
                screen.PutString(x, y, label.AsSpan(0, lblLen),
                    Color.LightGray, Color.DarkGray);
            return x + lblLen;
        }

        private static int DrawSeparator(ScreenBuffer screen, int x, int y, int max)
        {
            // U+00B7 MIDDLE DOT, padded by a space on each side.
            const string Sep = " · ";
            int len = Math.Min(Sep.Length, max - x);
            if (len > 0)
                screen.PutString(x, y, Sep.AsSpan(0, len),
                    Color.LightGray, Color.DarkGray);
            return x + len;
        }
    }
}
