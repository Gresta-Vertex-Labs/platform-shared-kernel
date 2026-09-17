using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Caching.FusionCache.Serialization;
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
        // TryAddEnumerable, not TryAddSingleton: ValidateDataAnnotations() above has already added an
        // IValidateOptions<CachingOptions>, so TryAddSingleton would silently skip this validator.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CachingOptions>, CachingOptionsValidator>());

        // Resolve CachingOptions synchronously so we can read SerializerContext before
        // building the DI container (options are configured above via Configure delegate).
        // We build a temporary options instance to check SerializerContext.
        var tempOptions = new CachingOptions();
        configure?.Invoke(tempOptions);

        // Build the JsonSerializerOptions for the FusionCache STJ serializer.
        // When SerializerContext is set, combine it with EncryptedCacheEntryJsonContext (for encrypted
        // entries) so FusionCache's L2 serializer is NativeAOT-safe.
        JsonSerializerOptions? resolvedJsonOptions = null;
        if (tempOptions.SerializerContext is not null)
        {
            resolvedJsonOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(
                    tempOptions.SerializerContext,
                    EncryptedCacheEntryJsonContext.Default),
            };
        }

        // Registered unconditionally (Phase 46/WO-081) so Encryption.EncryptedCacheService can
        // later reuse the exact same JsonSerializerOptions instance for its own T-to-plaintext-bytes
        // step — see CacheSerializationOptions' remarks — instead of re-deriving a second one.
        services.TryAddSingleton(new CacheSerializationOptions
        {
            Value = resolvedJsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web),
        });

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

        // Wire FusionCache with a dedicated MemoryCache whose SizeLimit is controlled by
        // CachingOptions.L1SizeLimit (an entry COUNT limit — each entry contributes Size = 1).
        // Providing the MemoryCache directly (not via WithRegisteredMemoryCache) ensures the
        // SizeLimit is isolated to this FusionCache instance and not shared with other
        // IMemoryCache consumers in the DI container.
        var sizeLimit = tempOptions.L1SizeLimit;
        services
            .AddFusionCache()
            .TryWithRegisteredLogger()
            .WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit }))
            .WithDefaultEntryOptions(o => o.Size = 1)
            .WithRegisteredSerializer();

        // Register ICacheService as a singleton backed by FusionCacheService.
        services.TryAddSingleton<ICacheService, FusionCacheService>();

        // One key provider serves both interfaces, so global and tenant keys share the single
        // validated CachingOptions.ServiceName. Consumers may override either registration.
        services.TryAddSingleton<CacheKeyProvider>();
        services.TryAddSingleton<ICacheKeyProvider>(sp => sp.GetRequiredService<CacheKeyProvider>());
        services.TryAddSingleton<ITenantCacheKeyProvider>(sp => sp.GetRequiredService<CacheKeyProvider>());

        return new CachingBuilder(services);
    }
}
