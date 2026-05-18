using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the SharedKernel caching
/// infrastructure.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SharedKernel caching infrastructure backed by FusionCache L1 in-process
    /// memory cache. Call <c>AddRedisL2</c> on the returned builder to also wire up the
    /// distributed Redis L2 backplane.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="CachingOptions"/>. When <see langword="null"/>
    /// the defaults are used.
    /// </param>
    /// <returns>
    /// A <see cref="ICachingBuilder"/> that can be used to chain additional registrations
    /// (e.g., <c>AddRedisL2</c>).
    /// </returns>
    public static ICachingBuilder AddSharedKernelCaching(
        this IServiceCollection services,
        Action<CachingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register and validate CachingOptions.
        var optionsBuilder = services
            .AddOptions<CachingOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Register custom validator for rules data annotations cannot express (e.g. ServiceName).
        services.TryAddSingleton<IValidateOptions<CachingOptions>, CachingOptionsValidator>();

        // Register FusionCache with STJ serializer and registered logger.
        services
            .AddFusionCache()
            .TryWithRegisteredLogger()
            .WithSystemTextJsonSerializer();

        // Register ICacheService as a singleton backed by FusionCacheService.
        services.TryAddSingleton<ICacheService, FusionCacheService>();

        // Register ICacheKeyProvider with the default platform-standard implementation.
        // Consumers may override by registering their own ICacheKeyProvider after this call.
        services.TryAddSingleton<ICacheKeyProvider, CacheKeyProvider>();

        return new CachingBuilder(services);
    }
}
