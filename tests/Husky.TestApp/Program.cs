using Husky.Client;

const string ModeEnvVar = "HUSKY_TESTAPP_MODE";
const string ModeNormal = "normal";
const string ModeSlowShutdown = "slow-shutdown";
const string ModeWait = "wait";
const string ModeCrash = "crash";

string mode = Environment.GetEnvironmentVariable(ModeEnvVar) ?? ModeNormal;

// Distinct stdout + stderr markers — the launcher's stdout/stderr forwarding tests
// pin on these strings.
Console.Out.WriteLine($"testapp: ready (mode={mode})");
Console.Out.Flush();
Console.Error.WriteLine("testapp: hello stderr");
Console.Error.Flush();

// 'wait' mode: do not attach, sleep forever. Used to exercise the launcher's
// hard-kill path without needing a pipe server in scope.
if (mode == ModeWait)
{
    await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
    return 0;
}

if (!HuskyClient.IsHosted)
{
    Console.Out.WriteLine("testapp: standalone — exiting cleanly.");
    return 0;
}

await using HuskyClient client = await HuskyClient.AttachAsync();

if (mode == ModeCrash)
{
    Console.Out.WriteLine("testapp: crash mode — exiting with code 7.");
    Console.Out.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(50));
    return 7;
}

switch (mode)
{
    case ModeSlowShutdown:
        // Register a handler that never returns within any reasonable launcher
        // shutdown window — exercises the launcher's hard-kill path.
        client.OnShutdown(async (_, _) =>
        {
            Console.Out.WriteLine("testapp: slow-shutdown handler entered — sleeping.");
            Console.Out.Flush();
            await Task.Delay(TimeSpan.FromMinutes(10), CancellationToken.None);
        });
        break;

    case ModeNormal:
    default:
        client.OnShutdown((reason, _) =>
        {
            Console.Out.WriteLine($"testapp: shutdown received (reason={reason}).");
            Console.Out.Flush();
            return Task.CompletedTask;
        });
        break;
}

Console.Out.WriteLine("testapp: attached, awaiting shutdown.");
Console.Out.Flush();

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, client.ShutdownToken);
}
catch (OperationCanceledException) { /* normal */ }

Console.Out.WriteLine("testapp: exiting.");
return 0;
