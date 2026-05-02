# Husky.Client

Slim client library for apps hosted by [Husky](https://github.com/Chloe3DX/husky), the generic .NET app launcher.

## Quick start

`Program.cs`:

```csharp
using Husky.Client;

await using HuskyClient? husky = await HuskyClient.AttachIfHostedAsync();

husky?.OnShutdown(async (reason, ct) =>
{
    // Drain queues, flush state, close sockets — whatever your app needs.
    await myApp.StopAsync(ct);
});

await myApp.RunAsync(husky?.ShutdownToken ?? CancellationToken.None);
```

That is it. If Husky is not present (e.g. running in the debugger), `AttachIfHostedAsync` returns `null` and the app keeps going standalone — every call to the client is `?.`-safe.

## ASP.NET Core / Generic Host

```csharp
using Husky.Client.DependencyInjection;

builder.Services.AddHuskyClient();
```

The hosted service:

- Owns the `HuskyClient` instance.
- Calls `IHostApplicationLifetime.StopApplication()` on `shutdown` so your `IHostedService.StopAsync` chain runs cleanly.
- Replies to `ping` probes with `HealthCheckService` (if you registered `AddHealthChecks()`), otherwise `Healthy`.

## What you get

- A `ShutdownToken` cancelled when Husky asks the app to stop (or the launcher disappears).
- A single shutdown handler hook with a `ShutdownReason` (`Update`, `Manual`, `LauncherStopping`).
- A health probe hook (`SetHealth`) — return `Healthy` / `Degraded` / `Unhealthy` plus arbitrary detail key/values.
- Standalone-mode safety — without Husky, all client APIs become no-ops.

## Wire-protocol version

Husky and Husky.Client must share the same wire-protocol version. The handshake refuses connections on mismatch. Major NuGet bumps map to wire-protocol bumps.

## License

TBD — same as the launcher.

🐺 *woof.*
