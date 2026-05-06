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
    /// 1-row strip at the bottom holding three focusable buttons:
    /// <c>[c] copy logs</c>, <c>[u] update now</c>, <c>[x] exit</c>.
    /// The bracketed letters double as global hotkeys handled by the
    /// chrome's <see cref="HuskyChrome.OnKey"/>; Tab/Shift+Tab cycles
    /// focus through them; Enter activates the focused button.
    /// </summary>
    private sealed class ActionBar : Container
    {
        // Cell widths chosen to fit each label plus one cell of padding on
        // either side; the focus highlight inverts the whole rectangle so
        // the gap matters visually.
        private const int CopyWidth   = 16; // " [c] copy logs  "
        private const int UpdateWidth = 18; // " [u] update now  "
        private const int ExitWidth   = 11; // " [x] exit  "
        private const int Gap         = 2;

        private readonly Button copyButton;
        private readonly Button updateButton;
        private readonly Button exitButton;

        public ActionBar(Action onCopy, Action onUpdate, Action onExit)
        {
            copyButton = new Button(" [c] copy logs ")
            {
                Foreground = Color.LightGray,
                Background = Color.DarkGray,
            };
            copyButton.Click += () => onCopy();

            updateButton = new Button(" [u] update now ")
            {
                Foreground = Color.LightGray,
                Background = Color.DarkGray,
            };
            updateButton.Click += () => onUpdate();

            exitButton = new Button(" [x] exit ")
            {
                Foreground = Color.LightGray,
                Background = Color.DarkGray,
            };
            exitButton.Click += () => onExit();

            Children.Add(copyButton);
            Children.Add(updateButton);
            Children.Add(exitButton);
        }

        protected override void ArrangeChildren()
        {
            var b = Bounds;
            // Left-aligned with one-cell margin and gaps; if the row is too
            // narrow, the buttons clip off the right edge — preferable to
            // truncating the labels themselves.
            int x = b.X + 1;
            copyButton.Bounds = new Rect(x, b.Y, CopyWidth, 1);
            x += CopyWidth + Gap;
            updateButton.Bounds = new Rect(x, b.Y, UpdateWidth, 1);
            x += UpdateWidth + Gap;
            exitButton.Bounds = new Rect(x, b.Y, ExitWidth, 1);
        }

        public override void OnDraw(ScreenBuffer screen)
        {
            // Fill the strip first so the gaps between buttons read as part
            // of the bar rather than as terminal-default empty cells.
            screen.FillRect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height,
                new Cell(' ', Color.LightGray, Color.DarkGray));

            ArrangeChildren();
            for (var i = 0; i < Children.Count; i++)
                Children[i].OnDraw(screen);
        }
    }
}
