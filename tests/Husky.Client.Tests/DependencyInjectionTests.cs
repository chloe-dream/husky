using Husky.Client;
using Husky.Client.DependencyInjection;
using Husky.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Husky.Client.Tests;

[Collection(EnvVarCollection.Name)]
public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddHuskyClient_registers_a_hosted_service()
    {
        ServiceCollection services = new();
        services.AddSingleton<IHostApplicationLifetime, FakeApplicationLifetime>();

        services.AddHuskyClient();

        await using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService[] hostedServices = [.. provider.GetServices<IHostedService>()];

        Assert.Single(hostedServices);
        Assert.IsType<HuskyHostedService>(hostedServices[0]);
    }

    [Fact]
    public void AddHuskyClient_throws_when_services_is_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HuskyServiceCollectionExtensions.AddHuskyClient(null!));
    }

    [Fact]
    public async Task HostedService_StartAsync_is_a_noop_in_standalone_mode()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, null);
        FakeApplicationLifetime lifetime = new();
        await using HuskyHostedService service = new(lifetime);

        await service.StartAsync(CancellationToken.None);

        Assert.False(lifetime.StopApplicationCalled);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopAsync_is_safe_to_call_without_StartAsync()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, null);
        FakeApplicationLifetime lifetime = new();
        HuskyHostedService service = new(lifetime);

        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => stopped.Token;

        public bool StopApplicationCalled { get; private set; }

        public void StopApplication()
        {
            StopApplicationCalled = true;
            stopping.Cancel();
        }

        public void Dispose()
        {
            started.Dispose();
            stopping.Dispose();
            stopped.Dispose();
        }
    }
}
