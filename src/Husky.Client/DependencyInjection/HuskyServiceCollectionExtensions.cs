using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Husky.Client.DependencyInjection;

public static class HuskyServiceCollectionExtensions
{
    /// <summary>
    /// Registers a hosted service that attaches to the Husky launcher when
    /// hosted (HUSKY_PIPE set) and bridges its shutdown signal to the host's
    /// IHostApplicationLifetime. In standalone mode the service is a no-op.
    ///
    /// Health reporting is wired automatically: if the application registers
    /// <c>AddHealthChecks()</c>, ping responses reflect the aggregated
    /// <see cref="HealthCheckService"/> result; otherwise pongs report
    /// <see cref="HealthState.Healthy"/>. To override this, attach to
    /// <see cref="HuskyClient"/> directly via
    /// <see cref="HuskyClient.AttachIfHostedAsync"/> and call
    /// <see cref="HuskyClient.SetHealth"/>.
    /// </summary>
    public static IServiceCollection AddHuskyClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<HuskyHostedService>();
        return services;
    }
}
