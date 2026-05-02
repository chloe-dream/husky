# Husky

> *your loyal app launcher* 🐺

Husky is a generic, configuration-driven launcher for .NET applications. It starts apps, keeps them alive, and installs updates automatically — without the app needing to contain any update logic itself.

**Status:** early development. v1.0 is being implemented from the spec in [LEASH.md](./LEASH.md).

---

## What it does

- Starts a .NET app and supervises it.
- Watches for new releases (GitHub Releases or a custom HTTPS manifest) and installs them automatically.
- Asks the app to shut down cleanly before each update, then swaps the binaries and starts it again.
- Restarts the app when it crashes (with sane backoff and a circuit breaker).
- Renders everything to a colored console — no log files, no GUI.
- Runs cross-platform: Windows + Linux, x64 and arm64.

## How an app integrates

Reference the `Husky.Client` NuGet package and add a few lines to `Program.cs`:

```csharp
await using var husky = await HuskyClient.AttachIfHostedAsync();
husky?.OnShutdown(async (reason, ct) => await app.StopAsync(ct));
```

That is it. The app keeps running standalone if Husky is not present.

## Project layout

```
src/
  Husky.Protocol/    shared wire contracts (named-pipe messages)
  Husky.Client/      NuGet package consumed by hosted apps
  Husky/             the launcher binary (Husky.exe)
tests/
  Husky.Protocol.Tests/
  Husky.Client.Tests/
  Husky.Tests/
```

## Deployment story

Ship `Husky.exe` plus a `husky.config.json`. On first run the launcher pulls the configured app from its update source (bootstrap install). From there on, every released update is installed in place during a brief cutover window.

```
<install-dir>/
  Husky.exe
  husky.config.json
  app/                ← installed and updated by Husky
  download/           ← last update package, kept for forensics
```

## The leash

The full specification lives in [LEASH.md](./LEASH.md). The working agreement for collaborators (and the AI agent driving most of the implementation) lives in [CLAUDE.md](./CLAUDE.md).

## License

[MIT](./LICENSE) — © 2026 Chloe Dream.

---

🐺 *woof.*
