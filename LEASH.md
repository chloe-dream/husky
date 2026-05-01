# LEASH · Husky Specification v1.0

> *The leash that binds.*

Husky is a generic, configuration-driven launcher for .NET applications. It starts apps, keeps them alive, and installs updates automatically — without the app needing to contain any update logic itself. Husky is cross-platform (Windows, Linux), lean, and has personality.

This document is the complete specification for an initial implementation. It is written so that a coding agent (e.g. Claude Code) can derive Husky from it.

---

## ⚠️ Language Rules

This specification lives in English so it can sit comfortably in the Git repository alongside the code.

**However:** the developer (Chloe / Marcus) communicates in **German**. All clarifications, design discussions, status updates, and explanations directed at the developer must be in **German**. Code remains **English-only** in every aspect: identifiers, comments, string literals, commits, READMEs.

See [CLAUDE.md](./CLAUDE.md) for the full working agreement.

---

## Table of Contents

1. [Goals and Non-Goals](#1-goals-and-non-goals)
2. [Architecture](#2-architecture)
3. [Husky.Protocol](#3-huskyprotocol)
4. [Husky.Client](#4-huskyclient)
5. [Husky (Launcher)](#5-husky-launcher)
6. [Runtime Directory Layout](#6-runtime-directory-layout)
7. [Update Flow in Detail](#7-update-flow-in-detail)
8. [Watchdog Logic](#8-watchdog-logic)
9. [Source Providers](#9-source-providers)
10. [Console Rendering](#10-console-rendering)
11. [Cross-Platform](#11-cross-platform)
12. [Error Handling](#12-error-handling)
13. [Build & Publishing](#13-build--publishing)
14. [Out of Scope](#14-out-of-scope)
15. [Glossary](#15-glossary)
16. [Appendix A — Recommended Implementation Order](#appendix-a--recommended-implementation-order)

---

## 1. Goals and Non-Goals

### 1.1 Goals

- **A generic, configurable launcher binary** for arbitrary .NET applications.
- **Self-managed updates** of the hosted app without restarting the launcher.
- **Clean shutdowns** with the app's own cleanup routine before every update or stop.
- **Minimal configuration** via a single JSON file with few fields.
- **Cross-platform** (Windows and Linux, x64 and arm64).
- **Character.** Modern, colored console with Husky branding. No enterprise look.
- **Easy app integration** — hosted apps reference a slim client library, three lines in `Program.cs`.

### 1.2 Non-Goals (for v1.0)

- No rollback. If an update is broken, it is broken — that is a release-process responsibility.
- No code signing of update packages.
- No self-update of the launcher itself.
- No file-based logging — everything goes to the console.
- No GUI.
- No multi-app management within a single launcher instance. One launcher instance = one app. Multiple apps → multiple launcher instances, each in its own directory.

---

## 2. Architecture

### 2.1 Solution Layout

```
Husky.sln
├── src/
│   ├── Husky.Protocol/      ← shared lib: messages, JSON contracts, pipe naming
│   ├── Husky.Client/        ← NuGet package for hosted apps
│   └── Husky/               ← the Husky.exe launcher itself
└── tests/
    ├── Husky.Protocol.Tests/
    ├── Husky.Client.Tests/
    └── Husky.Tests/
```

### 2.2 Communication Model

```
┌─────────────────────────┐         ┌──────────────────────────┐
│  Husky (Launcher)       │         │  Hosted App              │
│  ─────────────────      │         │  ─────────────────       │
│  - reads config         │         │  - uses Husky.Client     │
│  - polls update source  │ ◄──────►│    NuGet package         │
│  - manages app process  │  Named  │  - registers shutdown    │
│  - watches stdout       │  Pipe   │    + health callbacks    │
│  - applies updates      │         │  - normal app code       │
│  - renders console      │         │                          │
└─────────────────────────┘         └──────────────────────────┘
        │                                       │
        │ stdout/stderr piping                  │
        └───────────────────────────────────────┘
```

- **Named pipe** for control communication (shutdown requests, heartbeats, health probes, hello handshake).
- **stdout/stderr** as the log channel: whatever the app writes to the console, the launcher captures and renders with color.

### 2.3 Cross-Platform Strategy

- Named pipes work cross-platform in .NET via `NamedPipeServerStream` / `NamedPipeClientStream`.
- On Linux, pipes are realized internally as Unix domain sockets in `/tmp/CoreFxPipe_<name>`. Transparent to Husky.
- The only platform-specific piece is the **pipe ACL** (Windows: `PipeSecurity`; Linux: default filesystem permissions are sufficient).
- Service integration (Windows SCM, Linux systemd) is **not the launcher's job**. Husky runs as a regular console process. Whoever wants to run Husky as a service registers the Husky binary through the OS's mechanism (`sc.exe` / a systemd unit).

### 2.4 Target Framework

- **.NET 9.0**, with migration to .NET 10 once GA.
- No LTS bias.

---

## 3. Husky.Protocol

Shared library — common contracts for launcher and client.

### 3.1 Pipe Naming

- The launcher generates a GUID-based pipe name when starting the app: `husky-{guid}`.
- The pipe name is passed to the child app via environment variables:
  - `HUSKY_PIPE` → the pipe name (no path prefix, just the name).
  - `HUSKY_APP_NAME` → the name from the config (for logging).
- If `HUSKY_PIPE` is missing, the app runs without Husky (standalone mode).

### 3.2 Pipe Security

- **Windows**: `PipeSecurity` with a `PipeAccessRule` for `WindowsIdentity.GetCurrent().User`, full access. No access for other users.
- **Linux**: no extra configuration needed — the pipe socket file gets user-only permissions by default.

### 3.3 Wire Format

- **JSON Lines**: each message is exactly one line of UTF-8 JSON, terminated by `\n`.
- No BOM, no pretty-printed whitespace inside the message.
- Readers read line by line and parse each line as one message.

### 3.4 Base Message Schema

Every message has:

```json
{
  "id": "optional-correlation-guid",
  "replyTo": "optional-correlation-guid",
  "type": "message-type-string",
  "data": { ... }
}
```

- `id` is set when the sender expects a reply. GUID string, lower-case, no braces.
- `replyTo` is set when the message is a reply to a previous message; contains the `id` of the original.
- `type` is always set, identifies the message type (see below).
- `data` is optional, contains type-specific fields.

### 3.5 Message Types

#### 3.5.1 `hello` — App → Launcher

First message after pipe connect. App sends its identity.

```json
{
  "id": "...",
  "type": "hello",
  "data": {
    "protocolVersion": 1,
    "appVersion": "1.4.2",
    "appName": "umbrella-bot",
    "pid": 4218
  }
}
```

#### 3.5.2 `welcome` — Launcher → App

Reply to `hello`. Confirms acceptance.

```json
{
  "id": "...",
  "replyTo": "...",
  "type": "welcome",
  "data": {
    "protocolVersion": 1,
    "launcherVersion": "1.0.0",
    "accepted": true,
    "reason": null
  }
}
```

If `accepted: false`: `reason` contains plain-text explanation; the app should exit.

#### 3.5.3 `heartbeat` — App → Launcher

Periodic liveness signal. No reply expected.

```json
{ "type": "heartbeat" }
```

Send every **5 seconds**.

#### 3.5.4 `ping` — Launcher → App

Active health probe. The app must reply.

```json
{ "id": "...", "type": "ping" }
```

#### 3.5.5 `pong` — App → Launcher

Reply to `ping`. Includes health status.

```json
{
  "id": "...",
  "replyTo": "...",
  "type": "pong",
  "data": {
    "status": "healthy",
    "details": { "queue": 3, "guilds": 12 }
  }
}
```

- `status`: `"healthy"` | `"degraded"` | `"unhealthy"`
- `details`: opaque app-specific key-value pairs. The launcher only displays them in the console; it does not interpret them.

#### 3.5.6 `shutdown` — Launcher → App

Request to terminate.

```json
{
  "id": "...",
  "type": "shutdown",
  "data": {
    "reason": "update",
    "timeoutSeconds": 60
  }
}
```

- `reason`: `"update"` | `"manual"` | `"launcher-stopping"`
- `timeoutSeconds`: after this elapses, the launcher hard-kills the process.

#### 3.5.7 `shutdown-ack` — App → Launcher

Acknowledgement of the shutdown message. Send immediately, then start cleanup.

```json
{ "id": "...", "replyTo": "...", "type": "shutdown-ack" }
```

#### 3.5.8 `shutdown-progress` — App → Launcher (optional)

During cleanup, the app may optionally report progress.

```json
{
  "type": "shutdown-progress",
  "data": { "message": "flushing queue (3 items left)" }
}
```

### 3.6 Versioning

- `protocolVersion` is an integer.
- Current version: **1**.
- Incompatibility rule: launcher and app must have the exact same `protocolVersion`. On mismatch → `welcome.accepted = false`.
- Later versions may introduce additive fields without bumping (unknown fields are ignored). Breaking changes increment the version.

### 3.7 Implementation Notes

- Use `System.Text.Json` with source generators for AOT compatibility.
- Records with `[JsonPolymorphic]` and `[JsonDerivedType]` for the message hierarchy are clean, but for v1 a dispatch switch over `type` is sufficient.
- All records should use `init` properties, not mutable setters.

---

## 4. Husky.Client

NuGet package for hosted apps.

### 4.1 Public API (Target Shape)

```csharp
namespace Husky.Client;

public sealed class HuskyClient : IAsyncDisposable
{
    // Cheap synchronous probe — checks the HUSKY_PIPE env var only.
    public static bool IsHosted { get; }

    // Returns null if not hosted; otherwise performs the full async attach
    // (pipe connect + hello/welcome handshake + background loops).
    public static Task<HuskyClient?> AttachIfHostedAsync(CancellationToken ct = default);

    // Throws if not hosted. For callers that require Husky to be present.
    public static Task<HuskyClient> AttachAsync(CancellationToken ct = default);

    public CancellationToken ShutdownToken { get; }

    public string? AppName { get; }

    public void OnShutdown(Func<ShutdownReason, CancellationToken, Task> handler);

    public void SetHealth(Func<HealthStatus> provider);

    public ValueTask DisposeAsync();
}

public enum ShutdownReason { Update, Manual, LauncherStopping }

public enum HealthState { Healthy, Degraded, Unhealthy }

public sealed record HealthStatus(
    HealthState State,
    IReadOnlyDictionary<string, object>? Details = null
)
{
    public static HealthStatus Healthy { get; } = new(HealthState.Healthy);
    public HealthStatus With(string key, object value) => /* ... */;
}
```

### 4.2 ASP.NET Core / Generic Host Integration

```csharp
namespace Husky.Client.DependencyInjection;

public static class HuskyServiceCollectionExtensions
{
    public static IServiceCollection AddHuskyClient(this IServiceCollection services);
}
```

- Internally registers an `IHostedService` that builds and owns the `HuskyClient` instance.
- On `shutdown`: calls `IHostApplicationLifetime.StopApplication()` — the host runs its `IHostedService.StopAsync` routines cleanly.
- On `ping`: by default uses `HealthCheckService` if registered via `AddHealthChecks()`, otherwise default `Healthy`.
- The app code does not need to do anything else.

### 4.3 Lifecycle

1. `AttachIfHostedAsync()` checks `Environment.GetEnvironmentVariable("HUSKY_PIPE")`. If unset → returns `null`. (`AttachAsync()` throws in this case.)
2. If set: opens `NamedPipeClientStream` with the name, connects (timeout: 5s).
3. Sends `hello` with app metadata.
4. Awaits `welcome` (timeout: 5s). If `accepted: false` → throws; the app author decides what to do.
5. Starts two background tasks:
   - **Sender loop**: every 5s send `heartbeat`.
   - **Receiver loop**: reads incoming messages, dispatches by `type`.
6. On incoming `shutdown`:
   - Immediately sends `shutdown-ack`.
   - Calls the registered `OnShutdown` handler (with the `ShutdownReason` and a `CancellationToken` cancelled `timeoutSeconds - 5s` after start, giving cleanup time but cutting in just before the launcher's hard-kill).
   - Awaits handler completion.
   - Cancels `ShutdownToken`.
7. On incoming `ping`:
   - Calls the `SetHealth` provider (or default `Healthy` if not set).
   - Sends `pong` with the status.
8. On `DisposeAsync`: cleanly closes the pipe.

### 4.4 Robustness

- **Pipe disconnect detection**: if the pipe is closed by the launcher (launcher stopped/crashed), `ShutdownToken` fires with reason `LauncherStopping`. The app may react or simply exit.
- **No auto-reconnect** in v1. If the pipe is gone, it is gone.
- **Standalone mode**: if `AttachIfHosted()` returns `null`, all client methods are unreachable (`?.` is enough). The app runs normally.

### 4.5 Dependencies

- `Husky.Protocol` (project reference).
- `Microsoft.Extensions.Hosting.Abstractions` (for the DI extension).
- `System.Text.Json` (built-in).
- **No** other NuGet dependencies.

---

## 5. Husky (Launcher)

The executable launcher binary.

### 5.1 Responsibilities

1. Load and validate config.
2. Poll the update source.
3. Start and supervise the child process (the app).
4. Pipe stdout/stderr from the app into its own console, color-coded.
5. Run the named-pipe server, perform the hello handshake.
6. Run the watchdog loop.
7. Apply updates when a new version is available.
8. Crash-restart logic.
9. Render the console UI.

### 5.2 Configuration File

Path: `husky.config.json` in the same directory as `Husky.exe`.

**Schema:**

```json
{
  "name": "umbrella-bot",
  "executable": "app/UmbrellaBot.exe",

  "source": {
    "type": "github",
    "repo": "chloe/umbrella-bot",
    "asset": "UmbrellaBot-{version}.zip"
  },

  "checkMinutes": 60,
  "shutdownTimeoutSec": 60,
  "killAfterSec": 10,
  "restartAttempts": 3,
  "restartPauseSec": 30
}
```

**Fields:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | ✓ | Display name; appears in console and pipe naming. |
| `executable` | string | ✓ | Relative path to the app executable (relative to the `Husky.exe` directory). |
| `source` | object | ✓ | Update-source configuration; see §9. |
| `checkMinutes` | int | – | Polling interval for update checks. Default: `60`. Min: `5`. |
| `shutdownTimeoutSec` | int | – | How long the launcher waits for graceful shutdown. Default: `60`. |
| `killAfterSec` | int | – | Additional grace period after the timeout before hard-kill. Default: `10`. |
| `restartAttempts` | int | – | Crash restarts allowed per rolling hour. Default: `3`. |
| `restartPauseSec` | int | – | Pause between crash restarts. Default: `30`. |

**Validation:**

- Missing required fields: launcher exits with a clear console message and exit code `2`.
- Invalid JSON: exit code `2`.
- Executable not found: launcher attempts a **bootstrap update** via the configured source (§7.5). Only if the bootstrap fails does the launcher exit with code `2`.

### 5.3 Boot Sequence

1. Render the Husky ASCII logo + tagline.
2. Load and validate the config.
3. Determine the current app version:
   - If the executable exists: read from `FileVersionInfo.GetVersionInfo(executable).FileVersion`.
   - If the executable does **not** exist: enter **bootstrap mode** — treat the current version as `"0.0.0"` so any source version triggers an update.
4. Initialize the source provider based on `source.type`.
5. Initial update check (always on at startup):
   - **Bootstrap mode**: run the bootstrap update flow (§7.5). On failure: exit code `2`.
   - Otherwise, if a new version is available → run the update flow (§7), then start the app afterwards.
   - Otherwise → start the app (§5.4).
6. Start the watchdog loop.
7. Start the update-polling timer (`checkMinutes`).

### 5.4 App Start

1. Generate the pipe name: `husky-{guid}`.
2. Open `NamedPipeServerStream`, apply ACL (§3.2).
3. Build the `ProcessStartInfo`:
   - `FileName` = absolute path to the executable.
   - `WorkingDirectory` = the executable's directory.
   - `UseShellExecute = false`.
   - `RedirectStandardOutput = true`, `RedirectStandardError = true`.
   - `CreateNoWindow = true`.
   - Environment: set `HUSKY_PIPE` and `HUSKY_APP_NAME`.
4. `Process.Start()`.
5. Background task: read stdout lines and render to console (source: `app`).
6. Background task: read stderr lines and render to console (source: `app`, color red).
7. Wait for the pipe connect (timeout: 30s).
8. Receive `hello`, validate, reply with `welcome`.
9. Activate the watchdog.

### 5.5 App Stop (Graceful)

1. Send `shutdown` with the current `reason` and `shutdownTimeoutSec` as `timeoutSeconds`.
2. Await `shutdown-ack` (timeout: 5s). If absent: console warning, continue anyway.
3. Wait for process exit (timeout: `shutdownTimeoutSec`).
4. If still running: wait an additional `killAfterSec`.
5. If still running: `Process.Kill(entireProcessTree: true)`. Console warning in "growling" tone.
6. Close the pipe.

### 5.6 Console Control

- **Ctrl+C** or **SIGTERM**: Husky treats this as an app stop (`reason: "launcher-stopping"`), waits for clean exit, then exits itself.
- **Double Ctrl+C**: immediate hard-kill of the app, then exit.

---

## 6. Runtime Directory Layout

```
<install-dir>/
├── Husky.exe                  ← launcher binary
├── husky.config.json          ← configuration
├── app/                       ← current app version
│   ├── UmbrellaBot.exe
│   ├── data/                  ← app user data (DB, models, etc.) — NEVER touched by launcher
│   └── ...
└── download/                  ← last update package, kept for forensics
    ├── UmbrellaBot-1.4.2.zip
    └── extracted/
        └── ...
```

**Conventions:**

- `app/` contains *everything* the app needs at runtime, including user data, DBs, caches, local models.
- `download/` is transient. It is cleared before each new download. After a successful update, it remains for forensic inspection.
- There is **no** backup directory. There is **no** state file for version info — the current version is read from the executable's metadata.

---

## 7. Update Flow in Detail

### 7.1 Pre-Conditions

- The source provider has reported a new version (§9).
- The app is currently running.

### 7.2 Phase 1 — Preparation (App Keeps Running)

1. Clear `download/` (recursively delete its contents).
2. Download the ZIP from the source URL into `download/<asset-filename>`.
3. Verify SHA-256 if the source provides one. On mismatch: abort the update, console warning.
4. Extract the ZIP into `download/extracted/`.
5. Sanity check: does the executable exist at the expected path inside `extracted/`?
   - Expected path: identical to the configured `executable` path. So if `executable: "app/UmbrellaBot.exe"`, then `download/extracted/app/UmbrellaBot.exe` must exist.
6. **If Phase 1 fails**: abort the update, the app continues unaffected, retry on next check cycle.

### 7.3 Phase 2 — Cutover (App Downtime ≈ Seconds)

1. If the app is running: stop it gracefully (§5.5). On a boot-time update or a bootstrap install (§7.5) there is nothing to stop — skip this step.
2. Recursively copy files from `download/extracted/` into `<install-dir>/`:
   - Existing files are overwritten.
   - Existing files that are *not* in the update remain unchanged (critical: `app/data/` etc.).
   - New files from the update are created.
   - **Never delete** anything (not even files that existed in the old version but not in the new — the app cleans those up itself if it cares).
3. Start the app (§5.4).
4. Await `hello` (timeout: 30s).
5. On successful hello: log update success.
6. On missing hello: loud console warning. The watchdog continues normally; the crash-restart logic kicks in if needed.

### 7.4 Concurrent Updates

- While an update is in progress, further update checks are paused.
- No manual triggers in v1 (no user interface).

### 7.5 Bootstrap Update

When the launcher starts and finds no app installed at the configured `executable` path, it tries to install one from the configured source:

1. Run Phase 1 (§7.2) against the source.
2. Run Phase 2 (§7.3) — the stop step (Phase 2 step 1) is skipped because no app is running.
3. After a successful copy, the app is started normally (§5.4).
4. If the source returns no version, or Phase 1 fails for any reason: console error and exit code `2`.

Bootstrap is the **only** way to install an app from an empty `app/` directory in v1; there is no manual install command. Shipping just `Husky.exe` + `husky.config.json` and letting the launcher pull the app on first run is therefore a supported deployment story.

---

## 8. Watchdog Logic

### 8.1 Activity Tracking

The watchdog maintains a `lastActivity` timestamp for the app. It is reset to "now" on:

- Receipt of a `heartbeat` message.
- Receipt of any other message from the app.
- Any stdout or stderr output from the app.

### 8.2 Probing

- **If 10 minutes pass without any activity**: send an active `ping` to the app.
- Reply expected within **30 seconds**.
- On reply: `lastActivity` is reset, all is well.
- On timeout: increment the strike counter.

### 8.3 Escalation

- After **3 failed probes in a row**: the app is declared dead.
- Hard-kill (`Process.Kill`).
- Restart the app (§5.4) — this counts as one crash restart.

### 8.4 Crash-Restart Logic

- On process exit with a non-zero code (or a watchdog-triggered kill): restart.
- Maximum `restartAttempts` per **rolling one-hour window**.
- Pause between restarts: `restartPauseSec` seconds.
- If the limit is reached: Husky stops attempting restarts, remains in the "broken" state, visible in the console. Husky itself keeps running (in case a later update fixes the problem — but only passively, no further app starts without an update).
- On a successful new update: the restart counter resets and a start is attempted.

---

## 9. Source Providers

### 9.1 Provider Interface (Internal)

```csharp
internal interface IUpdateSource
{
    Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion, CancellationToken ct);
}

internal sealed record UpdateInfo(
    string Version,
    Uri DownloadUrl,
    string? Sha256
);
```

### 9.2 GitHub Provider

**Config:**

```json
"source": {
  "type": "github",
  "repo": "chloe/umbrella-bot",
  "asset": "UmbrellaBot-{version}.zip"
}
```

**Behavior:**

- Call: `GET https://api.github.com/repos/{repo}/releases/latest`.
- User-Agent header: `Husky/{version}`.
- Response includes `tag_name` (the version, possibly with a `v` prefix to be stripped) and `assets[]`.
- Asset match: the first asset whose name matches the `asset` pattern (placeholder `{version}` substituted).
- `DownloadUrl` is `assets[].browser_download_url` of the matched asset.
- `Sha256` is `null` (GitHub does not provide a hash by default — a future provider could fetch the digest via the assets API; skip in v1).
- Version comparison: SemVer-based.
- If the new version > current version: return `UpdateInfo`, otherwise `null`.

### 9.3 HTTP Provider

**Config:**

```json
"source": {
  "type": "http",
  "manifest": "https://chloe.neocities.org/x7k3p2-9f4q/umbrella/manifest.json"
}
```

**Manifest Format:**

```json
{
  "version": "1.4.3",
  "url": "https://chloe.neocities.org/x7k3p2-9f4q/umbrella/UmbrellaBot-1.4.3.zip",
  "sha256": "9b74c9897bac770ffc029102a200c5de"
}
```

**Behavior:**

- Call: `GET <manifest-url>`.
- User-Agent header: `Husky/{version}`.
- Parse the JSON, map fields directly to `UpdateInfo`.
- Version comparison: identical to GitHub.
- Auth: none. Security via non-public, hard-to-guess URLs ("security through obscurity" — explicitly accepted for non-public use).

### 9.4 Extensibility

- v1 has exactly these two providers, hardcoded with `if`/`switch` over `source.type`.
- Future versions may switch to a plugin architecture if needed.

---

## 10. Console Rendering

### 10.1 Library

- **Spectre.Console** (NuGet) for color, markup, progress bars.
- Cross-platform, ANSI-capable on modern terminals.

### 10.2 Greeting Banner

On startup: Husky ASCII logo + tagline. The concrete ASCII art is the designer's choice and is stored as a `const string` in the launcher code.

Example (placeholder — final art chosen by the author):

```
  [ice-blue]<husky-ascii-art>[/]

  [bold cyan]Husky[/] [dim]v1.0.0[/]
  [dim]your loyal app launcher[/]
```

### 10.3 Log Line Format

```
HH:mm:ss  <source>  <message>
```

- `HH:mm:ss`: time, dimmed (gray).
- `<source>`: fixed width 8 chars (right-padded), color-coded by source:
  - `husky` → cyan
  - `app` → green (stdout) / red (stderr)
  - `pipe` → dim (only when verbose-debug is opted in)
- `<message>`: default foreground, with status-word highlights (e.g. `up` green, `down` red, `degraded` yellow).

### 10.4 Husky Voice

Husky speaks tersely, like a dog — short, punchy, with the occasional `woof.`. But never in the way. Examples:

- Start: `woof. starting umbrella-bot`
- Update check: `sniffing for updates...`
- Update found: `new version found: v1.4.3`
- Download: `fetching... <progress-bar>`
- Shutdown: `asking app to sit.`
- Hard-kill: `app didn't respond. growling.` → `taking it down.`
- Restart: `back online.` or `woof. <appname> v<version> is up.`
- Crash limit reached: `enough. lying down.`

These are *suggestions* — the implementer is free to stay in the Husky voice as they see fit.

### 10.5 Progress Bars

During download: `Spectre.Console.ProgressContext` with live updates.

```
14:55:42  husky    fetching... ████████░░░░░░░░░  62%  (4.1/6.6 MB)
```

### 10.6 No File Logging

- Husky writes nothing to files. Everything goes to the console.
- Persistence is the operator's job: redirect output with OS tools (`> husky.log` / `journalctl`).

---

## 11. Cross-Platform

### 11.1 Platform Differences

| Aspect | Windows | Linux |
|--------|---------|-------|
| Pipe backend | real named pipe | Unix domain socket (`/tmp/CoreFxPipe_<name>`) |
| Pipe ACL | `PipeSecurity` with the current user | default filesystem permissions are sufficient |
| Process tree kill | `Process.Kill(entireProcessTree: true)` | same (.NET supports it from .NET 5+) |
| Service integration | not Husky's concern | not Husky's concern |

### 11.2 Platform Conditionals

- `OperatingSystem.IsWindows()` for Windows-specific branches.
- `[SupportedOSPlatform("windows")]` attributes on Windows-only methods.

### 11.3 Test Matrix

- At least: Win11 x64, Linux x64 (Ubuntu 24.04), Linux arm64 (Raspberry Pi OS).
- macOS is not an official target but should not be intentionally broken.

---

## 12. Error Handling

### 12.1 Launcher Errors

| Scenario | Behavior |
|----------|----------|
| Config missing/broken | banner + error message in console, exit code 2 |
| Executable not found | banner + error, exit code 2 |
| Update source unreachable | console warning, update check skipped, app keeps running |
| ZIP download failed | console warning, update aborted, app keeps running |
| ZIP hash mismatch | console warning, update aborted |
| ZIP extract error | console warning, update aborted |
| App start failed | crash-restart logic (§8.4) |
| Pipe connect timeout after app start | console warning; app keeps running without Husky link, watchdog runs on stdout activity only |
| App not responding to shutdown | hard-kill after timeout, console warning |

### 12.2 Client Library Errors

| Scenario | Behavior |
|----------|----------|
| `HUSKY_PIPE` not set | standalone mode, all API calls are no-ops |
| Pipe connect error | exception thrown; the app author may catch or ignore |
| Pipe disconnected at runtime | `ShutdownToken` fires with `LauncherStopping` |
| Hello handshake fails | exception from `AttachAsync()` |
| Inbound message cannot be parsed | library logs to stdout, ignores the message, continues |

---

## 13. Build & Publishing

### 13.1 Husky.exe (Launcher)

- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
- `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true`
- `dotnet publish -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true`

Output: a single binary `Husky.exe` (Windows) or `Husky` (Linux).

### 13.2 Husky.Client (NuGet)

- Standard `dotnet pack -c Release`.
- Multi-targeting `net9.0` (later `net10.0` to be added).
- `Husky.Protocol` as a project reference, packed into the NuGet (or shipped as a separate package — decide at actual release time).

### 13.3 Versioning

- SemVer throughout.
- Husky.Protocol-version and wire-protocol-version are independent concerns:
  - The wire-protocol version (§3.6) is the integer in hello/welcome.
  - NuGet package versions follow standard SemVer.

---

## 14. Out of Scope

Explicitly *not* in v1.0 — possible candidates for later:

- **App-initiated update trigger** (`request-update` message) for UI apps like Fishbowl that want a "Update now" button.
- **Update channels** (stable / beta / nightly).
- **Code signing** and Authenticode verification.
- **Self-update** of the launcher itself (via a bootstrap binary).
- **Multi-app management** in a single Husky instance.
- **MCP server integration** (Claude Code → Husky → apps). Will be a separate project that talks to Husky's pipes.
- **Rollback** to a previous version.
- **GUI / tray icon**.
- **Structured `log` message type** over the pipe — stdout is enough.
- **Capabilities negotiation** in hello — the library handles silent defaults if something is unimplemented.
- **Plugin architecture** for source providers.

---

## 15. Glossary

| Term | Definition |
|------|------------|
| **Launcher** | `Husky.exe` — the host process. |
| **Hosted app** | The application Husky starts and supervises. |
| **Pipe** | Named pipe / Unix domain socket for IPC between launcher and hosted app. |
| **Source / update source** | Where new versions are discovered (GitHub Releases / HTTP manifest). |
| **Manifest** | JSON document describing version, URL, hash (HTTP source). |
| **Strike** | A failed health probe. |
| **Cutover** | The brief moment during an update when the app is down. |
| **Standalone mode** | App runs without Husky (e.g. in the debugger). The library detects this and no-ops. |

---

## Appendix A — Recommended Implementation Order

A suggested order for the initial implementation:

1. Create the solution and project structure.
2. **Husky.Protocol**: records, JSON serialization, pipe-naming constants, tests.
3. **Husky.Client**: connect/hello/heartbeat, shutdown handler, `IsHosted` / `AttachIfHosted`. Tests with a mock pipe server.
4. **Husky** skeleton: config loading, process start/stop, stdout piping, pipe server, hello handler.
5. **Husky** watchdog: activity tracking, probes, escalation.
6. **Husky** update flow: phase 1 (download/extract), phase 2 (stop/copy/start).
7. **Husky** source providers: GitHub, then HTTP.
8. **Husky** console rendering: Spectre.Console, banner, log format, Husky voice.
9. **Husky** crash-restart logic.
10. End-to-end test: example app + Husky + simulated GitHub release.

---

*Husky — your loyal app launcher.* 🐺
