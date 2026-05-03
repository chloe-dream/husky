# Husky

> *your loyal app launcher* 🐺

Husky is a generic, configuration-driven launcher for .NET applications. It starts apps, keeps them alive, and installs updates — automatically by default, on demand if your app prefers — without your app needing to contain any update logic itself.

**Status:** v1.0 implementation underway against the spec in [LEASH.md](./LEASH.md).

---

## What it does

- Starts a .NET app and supervises it (heartbeat, ping, watchdog, crash-restart with circuit breaker).
- Watches for new releases (GitHub Releases or a custom HTTPS manifest) and installs them.
- Two update modes, chosen by the app: **auto** (apply on discovery) or **manual** (notify and wait for the app's "Update now" trigger). Both ride the same wire protocol.
- Asks the app to shut down cleanly before each update, then swaps the binaries and starts the new version.
- Renders everything to a colored console — no log files, no GUI.
- Cross-platform: Windows + Linux, x64 and arm64.

## Three roles, three perspectives

Read the section that matches what you're trying to do.

### 1. You're an **app author** — make your app Husky-compatible

Reference the [`Husky.Client`](./src/Husky.Client) NuGet package and add three lines to `Program.cs`:

```csharp
using Husky.Client;

await using HuskyClient? husky = await HuskyClient.AttachIfHostedAsync();
husky?.OnShutdown(async (reason, ct) => await app.StopAsync(ct));

await app.RunAsync(husky?.ShutdownToken ?? CancellationToken.None);
```

That's it. Without Husky around (e.g. running in the debugger), `AttachIfHostedAsync` returns `null` and your app runs standalone — every client call is `?.`-safe.

If your app has a settings UI and you want users to **opt out of automatic updates**, attach with manual mode:

```csharp
HuskyClient husky = await HuskyClient.AttachAsync(
    HuskyClientOptions.Default with { UpdateMode = HuskyUpdateMode.Manual });

husky.UpdateAvailable += (_, info) =>
    SettingsViewModel.NewVersionBadge = info.NewVersion;

OnUpdateNowButtonClicked = () => await husky.RequestUpdateAsync();
OnAutoUpdatesToggled = checked =>
    await husky.SetUpdateModeAsync(checked ? HuskyUpdateMode.Auto : HuskyUpdateMode.Manual);
```

The launcher will only push `update-available` to apps that actually declared the `manual-updates` capability — apps that don't know about manual updates keep getting the existing auto behavior.

See [`src/Husky.Client/README.md`](./src/Husky.Client/README.md) for the full client surface.

### 2. You're a **distributor** — ship your Husky-ready app to users

You publish releases via GitHub or any HTTPS host. Two things happen:

- The release artefact (a ZIP of your app) gets fetched on each update.
- A `husky.config.json` ships *with* the release, telling Husky how to install your app — relative executable path, polling cadence, restart policy, display name. **Your users don't author this.** You do, once, in your repo.

For GitHub: drop `husky.config.json` at the repo root — Husky reads it from `raw.githubusercontent.com/<owner>/<repo>/HEAD/husky.config.json`. You can also attach it as a release asset to version it per-release.

For HTTP: embed a `config:` block in your manifest:

```json
{
  "version": "1.4.3",
  "url": "https://example.invalid/MyApp-1.4.3.zip",
  "sha256": "9b74...",
  "config": {
    "name": "my-app",
    "executable": "app/MyApp.exe",
    "checkMinutes": 30
  }
}
```

See [`samples/`](./samples) for full templates.

### 3. You're a **user** — install and run someone's Husky-shipped app

Drop `Husky.exe` plus a tiny `husky.config.json` somewhere convenient. The minimum:

```json
{
  "source": {
    "type": "github",
    "repo": "chloe-dream/the-fishbowl",
    "asset": "Fishbowl-{version}.zip"
  }
}
```

That's all. On first run Husky pulls the app's config from the source, downloads the latest release, and starts it. From there: auto-updates (or manual, if the app prefers).

If you want to override the polling cadence or any other setting the distributor chose, just add it to your local file — local always wins.

---

## How config resolves

Three layers, highest priority first:

1. **Local `husky.config.json`** — what *you* author. Only `source` is required; everything else is an optional override.
2. **Source-supplied config** — what the *app author* ships. Pulled from a GitHub asset / repo file or embedded in the HTTP manifest.
3. **Built-in defaults** — Husky's fallbacks for the timing knobs.

Field-by-field merge: a non-null value at a higher layer fills the slot. The minimum local config is `{ "source": { ... } }` whenever the source supplies `name` and `executable`.

## Runtime layout

```
<install-dir>/
  Husky.exe                  ← launcher binary
  husky.config.json          ← local config (just `source` may be enough)
  app/                       ← installed and updated by Husky
    YourApp.exe
    data/                    ← your app's data, never touched by Husky
  download/                  ← last update package, kept for forensics
```

## Project layout

```
src/
  Husky.Protocol/    shared wire contracts (named-pipe messages)
  Husky.Client/      NuGet package consumed by hosted apps
  Husky/             the launcher binary (Husky.exe)
tests/
  Husky.Protocol.Tests/
  Husky.Client.Tests/
  Husky.Tests/       includes the end-to-end suite
  Husky.TestApp/     a real .NET app with toggleable behaviours for E2E
```

## The leash

The full specification lives in [LEASH.md](./LEASH.md). The working agreement for collaborators (and the AI agent driving most of the implementation) lives in [CLAUDE.md](./CLAUDE.md).

## License

[MIT](./LICENSE) — © 2026 Chloe Dream.

---

🐺 *woof.*
