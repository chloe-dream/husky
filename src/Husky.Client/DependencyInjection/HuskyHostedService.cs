using Microsoft.Extensions.Hosting;

namespace Husky.Client.DependencyInjection;

internal sealed class HuskyHostedService(IHostApplicationLifetime lifetime) : IHostedService, IAsyncDisposable
{
    private HuskyClient? client;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client = await HuskyClient.AttachIfHostedAsync(cancellationToken).ConfigureAwait(false);
        if (client is null) return;

        client.OnShutdown((_, _) =>
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            client = null;
        }
    }
}
