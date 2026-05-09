using Retro.Crt;
using Retro.Crt.Input;
using Retro.Crt.Tui;
using Retro.Crt.Tui.Layout;
using Retro.Crt.Tui.Widgets;

namespace Husky;

/// <summary>
/// Root container for <see cref="HuskyApp"/>. Lays out the five rows
/// described in LEASH S10.4: a 1-row header, a 1-row separator, the
/// fill-the-rest log viewport, a 1-row separator, and a 1-row action
/// bar. Owns the chrome-level hotkeys (c/u/x/Esc/Ctrl+C) so users can
/// drive the TUI from the keyboard alone.
/// </summary>
internal sealed class HuskyChrome : Container
{
    private readonly Action drainPending;
    private readonly Action onCopyRequested;
    private readonly Action onUpdateRequested;
    private readonly Action onExitRequested;
    private readonly StackPanel body;
    private readonly HeaderView header;
    private readonly ActionBar actionBar;

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
        actionBar = new ActionBar(onCopyRequested, onUpdateRequested, onExitRequested);

        // Black-on-black chrome with two horizontal rules instead of
        // bg-fill bands. We trade two rows of log viewport for the
        // visual breathing room of a fully unified background.
        body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Sizes       =
            {
                LayoutSize.Cells(1), // header
                LayoutSize.Cells(1), // top separator
                LayoutSize.Star(),   // log
                LayoutSize.Cells(1), // bottom separator
                LayoutSize.Cells(1), // action bar
            },
            Children    = { header, new Separator(), log, new Separator(), actionBar },
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

    /// <summary>
    /// Override the header's right slot with a crash-restart message
    /// (e.g., <c>down - restarting in 3s</c>). Pass <c>null</c> to clear
    /// and revert to the health slot. Safe from any thread.
    /// </summary>
    public void SetCrashRestart(string? message)
    {
        header.SetCrashRestart(message);
        MarkDirty();
    }

    /// <summary>Update the action-bar <c>[u]</c> visibility / styling.</summary>
    public void SetUpdateActionState(UpdateActionState state)
    {
        actionBar.SetUpdateActionState(state);
        MarkDirty();
    }

    /// <summary>
    /// Replace the action bar's hotkey hints with a transient toast for
    /// <paramref name="duration"/> (default 3s, per LEASH S10.4). Safe
    /// from any thread; the bar reverts on its own when the timer fires.
    /// </summary>
    public void ShowActionBarToast(string text, Color color, TimeSpan? duration = null)
    {
        actionBar.ShowToast(text, color, duration ?? TimeSpan.FromSeconds(3));
        MarkDirty();
    }

    protected override void ArrangeChildren()
    {
        body.Bounds = Bounds;
    }

    public override void OnDraw(ScreenBuffer screen)
    {
        // Drain queued log entries before the children read LogViewer.Items -
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

        // Esc / x / Ctrl+C -> graceful exit. Two press of the latter could
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
            // Plain letters trigger the corresponding action - no Ctrl/Alt.
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
    /// name+version centered (or '(starting...)' before the handshake),
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
        // Set during a crash-restart pause (LEASH S10.4). When non-null
        // it takes priority over `health` for the right slot and renders
        // in red - the launcher tickles it once a second with the
        // remaining countdown text.
        private string? crashRestart;

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

        public void SetCrashRestart(string? message)
        {
            lock (stateLock) { crashRestart = message; }
            MarkDirty();
        }

        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            string? localName, localVer, localHealth, localCrashRestart;
            lock (stateLock)
            {
                localName = appName;
                localVer = appVersion;
                localHealth = health;
                localCrashRestart = crashRestart;
            }

            // Black across the whole chrome - the LogViewer is also black,
            // and the two horizontal separators above/below the log do the
            // visual delimiting that the bars used to do via DarkGray fill.
            // Status-word semantic colours (LightGreen / Yellow / LightRed)
            // pop on black instead of fighting a DarkGray bg.
            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.LightGray, Color.Black));

            // Left: launcher branding in the husky accent colour.
            string left = $" husky v{launcherVersion}";
            int leftLen = Math.Min(left.Length, b.Width);
            screen.PutString(b.X, b.Y, left.AsSpan(0, leftLen),
                Color.LightCyan, Color.Black, CellAttrs.Bold);

            // Right: crash-restart override wins over live health. The
            // right slot stays empty pre-handshake; '(starting...)' lives
            // in the center per S10.4 so the bar is symmetrical even
            // before the first hello.
            (string text, Color color) right = ResolveRightSlot(localHealth, localCrashRestart);
            int rightLen = Math.Min(right.text.Length, b.Width - leftLen);
            int rightX = b.X + b.Width - rightLen;
            if (rightLen > 0 && rightX >= b.X + leftLen)
            {
                screen.PutString(rightX, b.Y, right.text.AsSpan(0, rightLen),
                    right.color, Color.Black, CellAttrs.Bold);
            }

            // Middle: app name + version, centered between the left and right
            // slots. Pre-handshake (S10.4) the centre reads '(starting...)'
            // until the first hello populates name/version. The slot drops
            // entirely when it can't fit the full text - a truncated
            // app-name is worse than no name.
            string mid = localName is null
                ? "(starting...)"
                : (localVer is null ? localName : $"{localName} v{localVer}");
            Color midColor = localName is null ? Color.DarkGray : Color.White;
            int leftEnd = b.X + leftLen;
            int rightStart = rightLen > 0 ? rightX : b.X + b.Width;
            int slotStart = leftEnd + 1;
            int slotEnd = rightStart - 1;
            int slotWidth = slotEnd - slotStart;
            if (slotWidth >= mid.Length)
            {
                int midX = slotStart + (slotWidth - mid.Length) / 2;
                screen.PutString(midX, b.Y, mid.AsSpan(),
                    midColor, Color.Black, CellAttrs.Bold);
            }
        }

        private static (string text, Color color) ResolveRightSlot(string? health, string? crashRestart)
        {
            if (crashRestart is not null)
                return ($" {crashRestart} ", Color.LightRed);
            if (health is not null)
                return ($" {health} ", HealthColour(health));
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
    /// dot-separated hotkey hints (<c>c copy logs | u update now | x exit</c>).
    /// The hotkey letter is in the launcher's accent colour and bold; the
    /// label is plain on a black background; the middle-dot separator picks
    /// up the label colour. Activation goes through
    /// <see cref="HuskyChrome.OnKey"/>'s global hotkeys - there are no
    /// focusable button widgets here, since Tab cycling adds nothing
    /// when every command already has a single-letter shortcut.
    /// </summary>
    private sealed class ActionBar(Action onCopy, Action onUpdate, Action onExit) : View
    {
        private readonly Action onCopy = onCopy;
        private readonly Action onUpdate = onUpdate;
        private readonly Action onExit = onExit;

        private readonly object stateLock = new();
        // [u] visibility / styling, driven by the launcher's view of the
        // connected app's capabilities + the cached UpdateInfo.
        private UpdateActionState updateState;
        // Transient status replacement that takes the bar over for a few
        // seconds (e.g., the [c] copy-logs confirmation). Supersession
        // is handled with a monotonically-incrementing generation
        // counter: each ShowToast bumps it, and the old fire-and-forget
        // clear-task only acts when its generation still matches.
        private int toastGeneration;
        private string? toastText;
        private Color toastColor;

        public void SetUpdateActionState(UpdateActionState state)
        {
            lock (stateLock) updateState = state;
            MarkDirty();
        }

        public void ShowToast(string text, Color color, TimeSpan duration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            int generation;
            lock (stateLock)
            {
                generation = ++toastGeneration;
                toastText = text;
                toastColor = color;
            }
            MarkDirty();
            _ = ClearAfterAsync(generation, duration);
        }

        private async Task ClearAfterAsync(int generation, TimeSpan duration)
        {
            try { await Task.Delay(duration).ConfigureAwait(false); }
            catch { return; }

            lock (stateLock)
            {
                // Bail if a fresh ShowToast superseded us during the wait.
                if (toastGeneration != generation) return;
                toastText = null;
                toastColor = default;
            }
            MarkDirty();
        }

        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;

            UpdateActionState localState;
            string? localToast;
            Color localToastColor;
            lock (stateLock)
            {
                localState = updateState;
                localToast = toastText;
                localToastColor = toastColor;
            }

            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell(' ', Color.LightGray, Color.Black));

            if (localToast is not null)
            {
                int toastLen = Math.Min(localToast.Length, b.Width - 2);
                if (toastLen > 0)
                    screen.PutString(b.X + 1, b.Y, localToast.AsSpan(0, toastLen),
                        localToastColor, Color.Black, CellAttrs.Bold);
                _ = onCopy; _ = onUpdate; _ = onExit;
                return;
            }

            int x = b.X + 1;
            int max = b.X + b.Width;
            x = DrawCommand(screen, x, b.Y, max, "c", "copy logs", enabled: true);
            if (localState != UpdateActionState.Hidden)
            {
                x = DrawDotSeparator(screen, x, b.Y, max);
                x = DrawCommand(screen, x, b.Y, max, "u", "update now",
                    enabled: localState == UpdateActionState.Enabled);
            }
            x = DrawDotSeparator(screen, x, b.Y, max);
            DrawCommand(screen, x, b.Y, max, "x", "exit", enabled: true);

            _ = onCopy; _ = onUpdate; _ = onExit;
        }

        private static int DrawCommand(
            ScreenBuffer screen, int x, int y, int max, string hotkey, string label, bool enabled)
        {
            // Disabled state dims both the hotkey letter and the label so
            // the entry reads as 'present but inert' on the black bg.
            // Hotkey stays bold to keep the rhythm.
            Color hotkeyColor = enabled ? Color.LightCyan : Color.DarkGray;
            Color labelColor  = enabled ? Color.LightGray : Color.DarkGray;

            int hkLen = Math.Min(hotkey.Length, max - x);
            if (hkLen > 0)
                screen.PutString(x, y, hotkey.AsSpan(0, hkLen),
                    hotkeyColor, Color.Black, CellAttrs.Bold);
            x += hkLen;

            if (x < max)
            {
                screen.PutString(x, y, " ".AsSpan(), labelColor, Color.Black);
                x += 1;
            }

            int lblLen = Math.Min(label.Length, max - x);
            if (lblLen > 0)
                screen.PutString(x, y, label.AsSpan(0, lblLen),
                    labelColor, Color.Black);
            return x + lblLen;
        }

        private static int DrawDotSeparator(ScreenBuffer screen, int x, int y, int max)
        {
            // ASCII pipe between hotkey hints. Space on each side keeps
            // the bar readable in proportional fonts and in copies.
            const string Sep = " | ";
            int len = Math.Min(Sep.Length, max - x);
            if (len > 0)
                screen.PutString(x, y, Sep.AsSpan(0, len),
                    Color.LightGray, Color.Black);
            return x + len;
        }
    }

    /// <summary>
    /// 1-row horizontal rule used between the header / log / action-bar
    /// rows in place of bg-fill bands. Plain ASCII hyphen across the
    /// full width, dim against the chrome's black background - survives
    /// any clipboard / encoding round-trip on its way out of the TUI.
    /// </summary>
    private sealed class Separator : View
    {
        public override void OnDraw(ScreenBuffer screen)
        {
            var b = Bounds;
            if (b.Width <= 0 || b.Height <= 0) return;
            screen.FillRect(b.X, b.Y, b.Width, b.Height,
                new Cell('-', Color.DarkGray, Color.Black));
        }
    }
}
