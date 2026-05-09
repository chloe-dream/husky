using System.IO.Pipes;
using System.Text.Json;
using Husky.Client;
using Husky.Client.DependencyInjection;
using Husky.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using AspNetHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

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
        ServiceCollection services = new();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HuskyHostedService service = new(lifetime, provider);

        await service.StartAsync(CancellationToken.None);

        Assert.False(lifetime.StopApplicationCalled);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_StopAsync_is_safe_to_call_without_StartAsync()
    {
        using EnvVarScope _ = new(HuskyEnvironment.PipeNameVariable, null);
        FakeApplicationLifetime lifetime = new();
        ServiceCollection services = new();
        await using ServiceProvider provider = services.BuildServiceProvider();
        HuskyHostedService service = new(lifetime, provider);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedService_bridges_HealthCheckService_to_pong()
    {
        // Set up a launcher-side server pipe; point HUSKY_PIPE at it.
        string pipeName = $"husky-test-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = new(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, inBufferSize: 65_536, outBufferSize: 65_536);
        Task connectTask = server.WaitForConnectionAsync();

        using EnvVarScope pipeEnv = new(HuskyEnvironment.PipeNameVariable, pipeName);
        using EnvVarScope appEnv = new(HuskyEnvironment.AppNameVariable, "test-app");

        // Register a HealthCheckService with one Degraded check.
        ServiceCollection services = new();
        services.AddLogging();
        services.AddHealthChecks().AddCheck("custom", () => HealthCheckResult.Degraded("warming up"));
        await using ServiceProvider provider = services.BuildServiceProvider();

        FakeApplicationLifetime lifetime = new();
        await using HuskyHostedService service = new(lifetime, provider);

        // Start the hosted service — it will AttachIfHostedAsync against our pipe.
        Task startTask = service.StartAsync(CancellationToken.None);
        await connectTask;

        using MessageReader serverReader = new(server, leaveOpen: true);
        await using MessageWriter serverWriter = new(server, leaveOpen: true);

        // Handshake server-side: read hello, send welcome.
        MessageEnvelope? hello = await serverReader.ReadAsync();
        await SendWelcomeAsync(serverWriter, hello!.Id);

        await startTask;

        // Drain heartbeats until we can ping and read the pong with a degraded status.
        // The poll loop runs every 5s; we may need to wait for at least one cycle.
        string pongStatus = await PingUntilStatusAsync(serverReader, serverWriter, "degraded");

        Assert.Equal("degraded", pongStatus);

        await service.StopAsync(CancellationToken.None);
    }

    private static async Task<string> PingUntilStatusAsync(
        MessageReader reader, MessageWriter writer, string expectedStatus)
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            string pingId = Guid.NewGuid().ToString("D");
            await writer.WriteAsync(new MessageEnvelope { Id = pingId, Type = MessageTypes.Ping }, cts.Token);

            while (true)
            {
                MessageEnvelope? envelope = await reader.ReadAsync(cts.Token);
                if (envelope is null) throw new IOException("pipe closed waiting for pong");
                if (envelope.Type != MessageTypes.Pong) continue;

                PongPayload? pong = envelope.Data?.Deserialize(HuskyJsonContext.Default.PongPayload);
                if (pong is null) throw new InvalidOperationException("pong with no payload");
                if (pong.Status == expectedStatus) return pong.Status;
                break; // got pong but not the expected status yet — wait and ping again
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
        }
        throw new TimeoutException($"pong with status='{expectedStatus}' did not arrive in time");
    }

    private static async Task SendWelcomeAsync(MessageWriter writer, string? helloId)
    {
        WelcomePayload payload = new(
            ProtocolVersion: ProtocolVersion.Current,
            LauncherVersion: "1.0.0-test",
            Accepted: true,
            Reason: null);

        JsonElement data = JsonSerializer.SerializeToElement(payload, HuskyJsonContext.Default.WelcomePayload);
        await writer.WriteAsync(new MessageEnvelope
        {
            Id = Guid.NewGuid().ToString("D"),
            ReplyTo = helloId,
            Type = MessageTypes.Welcome,
            Data = data,
        });
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
