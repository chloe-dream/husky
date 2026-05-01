using Microsoft.Extensions.DependencyInjection;

namespace Husky.Client.DependencyInjection;

public static class HuskyServiceCollectionExtensions
{
    /// <summary>
    /// Registers a hosted service that attaches to the Husky launcher when
    /// hosted (HUSKY_PIPE set) and bridges its shutdown signal to the host's
    /// IHostApplicationLifetime. In standalone mode the service is a no-op.
    ///
    /// Health reporting defaults to Healthy. For custom health, attach to
    /// HuskyClient yourself via <see cref="HuskyClient.AttachIfHostedAsync"/>
    /// and call <see cref="HuskyClient.SetHealth"/>.
    /// </summary>
    public static IServiceCollection AddHuskyClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<HuskyHostedService>();
        return services;
    }
}
