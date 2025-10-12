using Microsoft.Agents.AI.DevUI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Agents.AI.DevUI.Extensions;

/// <summary>
/// Extension methods for adding DevUI services to the DI container
/// </summary>
public static class DevUIServiceCollectionExtensions
{
    /// <summary>
    /// Adds DevUI services to the service collection with configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration section (typically from appsettings.json)</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDevUI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DevUIOptions>(configuration);
        return services.AddDevUICore();
    }

    /// <summary>
    /// Adds DevUI services to the service collection with programmatic configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Action to configure DevUI options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDevUI(
        this IServiceCollection services,
        Action<DevUIOptions> configure)
    {
        services.Configure(configure);
        return services.AddDevUICore();
    }

    /// <summary>
    /// Adds DevUI services with default configuration
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDevUI(this IServiceCollection services)
    {
        services.Configure<DevUIOptions>(_ => { });
        return services.AddDevUICore();
    }

    /// <summary>
    /// Core DevUI service registration
    /// </summary>
    private static IServiceCollection AddDevUICore(this IServiceCollection services)
    {
        // Register DevUI core services as singletons
        services.TryAddSingleton<EntityDiscoveryService>();
        services.TryAddSingleton<MessageMapperService>();
        services.TryAddSingleton<ExecutionService>();
        services.TryAddSingleton<ConversationService>();

        // Register the hosted service that will discover entities from DI
        services.AddHostedService<DevUIHostedService>();

        // Add controllers for DevUI API endpoints
        services.AddControllers()
            .AddApplicationPart(typeof(DevUIServiceCollectionExtensions).Assembly);

        return services;
    }
}
