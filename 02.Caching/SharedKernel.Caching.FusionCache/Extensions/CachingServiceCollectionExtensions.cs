using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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

        // Resolve CachingOptions synchronously so we can read SerializerContext before
        // building the DI container (options are configured above via Configure delegate).
        // We build a temporary options instance to check SerializerContext.
        var tempOptions = new CachingOptions();
        configure?.Invoke(tempOptions);

        // Build the JsonSerializerOptions for the FusionCache STJ serializer.
        // When SerializerContext is set, combine it with the internal CacheInvalidationMessage
        // context so FusionCache's L2 serializer is fully NativeAOT-safe.
        JsonSerializerOptions? resolvedJsonOptions = null;
        if (tempOptions.SerializerContext is not null)
        {
            resolvedJsonOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    tempOptions.SerializerContext,
                    CacheInvalidationMessageJsonContext.Default),
            };
        }

        // Register IFusionCacheSerializer in DI (and the concrete type separately) so that
        // AddBrotliCompression can Replace IFusionCacheSerializer with a decorator factory that
        // resolves the concrete STJ serializer as its inner without creating a circular dependency.
        // AddFusionCacheSystemTextJsonSerializer registers via a factory — the concrete type
        // registration below ensures BrotliCacheSerializer can resolve it by type, not by interface.
        if (resolvedJsonOptions is not null)
        {
            services.TryAddSingleton(new FusionCacheSystemTextJsonSerializer(resolvedJsonOptions));
            services.AddFusionCacheSystemTextJsonSerializer(resolvedJsonOptions);
        }
        else
        {
            services.TryAddSingleton<FusionCacheSystemTextJsonSerializer>();
            services.AddFusionCacheSystemTextJsonSerializer();
        }

        // Wire FusionCache to resolve its serializer from the DI container so that any
        // post-registration decoration (e.g. AddBrotliCompression) is picked up automatically.
        services
            .AddFusionCache()
            .TryWithRegisteredLogger()
            .WithRegisteredSerializer();

        // Register ICacheService as a singleton backed by FusionCacheService.
        services.TryAddSingleton<ICacheService, FusionCacheService>();

        // Register ICacheKeyProvider with the default platform-standard implementation.
        // Consumers may override by registering their own ICacheKeyProvider after this call.
        services.TryAddSingleton<ICacheKeyProvider, CacheKeyProvider>();

        return new CachingBuilder(services);
    }
}
