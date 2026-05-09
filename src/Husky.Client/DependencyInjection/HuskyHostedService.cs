using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using AspNetHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

namespace Husky.Client.DependencyInjection;

internal sealed class HuskyHostedService(
    IHostApplicationLifetime lifetime,
    IServiceProvider services) : IHostedService, IAsyncDisposable
{
    private readonly HealthCheckService? healthCheckService = services.GetService<HealthCheckService>();
    private readonly HuskyClientOptions clientOptions = services.GetService<HuskyClientOptions>() ?? HuskyClientOptions.Default;
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(5);

    private HuskyClient? client;
    private CancellationTokenSource? healthPollCts;
    private Task? healthPollTask;
    private volatile HealthStatus cachedHealth = HealthStatus.Healthy;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client = await HuskyClient.AttachIfHostedAsync(clientOptions, cancellationToken).ConfigureAwait(false);
        if (client is null) return;

        client.OnShutdown((_, _) =>
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        });

        if (healthCheckService is not null)
        {
            client.SetHealth(() => cachedHealth);
            healthPollCts = new CancellationTokenSource();
            CancellationToken pollToken = healthPollCts.Token;
            healthPollTask = Task.Run(() => PollHealthAsync(healthCheckService, pollToken), CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopHealthPollAsync().ConfigureAwait(false);

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopHealthPollAsync().ConfigureAwait(false);

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    private async Task StopHealthPollAsync()
    {
        if (healthPollCts is null) return;

        try { await healthPollCts.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }

        if (healthPollTask is not null)
        {
            try { await healthPollTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { /* poll loop did not drain — proceed */ }
            catch (OperationCanceledException) { /* normal */ }
        }

        healthPollCts.Dispose();
        healthPollCts = null;
        healthPollTask = null;
    }

    private async Task PollHealthAsync(HealthCheckService service, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    HealthReport report = await service.CheckHealthAsync(ct).ConfigureAwait(false);
                    cachedHealth = MapReport(report);
                }
                catch when (!ct.IsCancellationRequested)
                {
                    cachedHealth = new HealthStatus(HealthState.Unhealthy);
                }

                try
                {
                    await Task.Delay(HealthPollInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
            }
        }
        catch (OperationCanceledException) { /* normal */ }
    }

    private static HealthStatus MapReport(HealthReport report)
    {
        HealthState state = report.Status switch
        {
            AspNetHealthStatus.Healthy => HealthState.Healthy,
            AspNetHealthStatus.Degraded => HealthState.Degraded,
            _ => HealthState.Unhealthy,
        };

        if (report.Entries.Count == 0)
            return new HealthStatus(state);

        Dictionary<string, object> details = new(report.Entries.Count);
        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            details[entry.Key] = entry.Value.Status.ToString().ToLowerInvariant();
        }
        return new HealthStatus(state, details);
    }
}
