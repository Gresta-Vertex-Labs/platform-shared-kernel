using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Caching.FusionCache.Serialization;
using SharedKernel.Configuration.Extensions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods that register the FusionCache implementation of
/// the SharedKernel caching contracts.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheService"/>, <see cref="ICacheKeyProvider"/> and
    /// <see cref="ITenantCacheKeyProvider"/>, with <see cref="CachingOptions"/> bound from the
    /// <c>SharedKernel:Caching</c> section of <paramref name="configuration"/> and validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root that contains the <c>SharedKernel:Caching</c> section.</param>
    /// <param name="configure">Optional changes applied after binding, for settings such as <see cref="CachingOptions.SerializerContext"/> that configuration cannot express.</param>
    /// <returns>A builder for chaining provider features such as <c>AddRedisL2</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(builder.Configuration);
    /// builder.Services
    ///     .AddSharedKernelCaching(builder.Configuration)
    ///     .AddTenantCacheService()
    ///     .AddRedisL2();
    /// </code>
    /// </example>
    public static ICachingBuilder AddSharedKernelCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CachingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CachingOptions, CachingOptionsValidator>(configuration, validateDataAnnotations: true);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return AddCore(services);
    }

    /// <summary>
    /// Registers <see cref="ICacheService"/>, <see cref="ICacheKeyProvider"/> and
    /// <see cref="ITenantCacheKeyProvider"/>, with <see cref="CachingOptions"/> set in code and
    /// validated at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options; must at least set <see cref="CachingOptions.ServiceName"/>.</param>
    /// <returns>A builder for chaining provider features such as <c>AddRedisL2</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddSharedKernelCaching(o => o.ServiceName = "orders");
    /// </code>
    /// </example>
    public static ICachingBuilder AddSharedKernelCaching(
        this IServiceCollection services,
        Action<CachingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<CachingOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CachingOptions>, CachingOptionsValidator>());

        return AddCore(services);
    }

    private static CachingBuilder AddCore(IServiceCollection services)
    {
        // One JsonSerializerOptions instance, built from the validated options, serves both FusionCache's
        // distributed serializer and the plaintext step of EncryptedCacheService.
        services.TryAddSingleton(sp => new CacheSerializationOptions
        {
            Value = CreateJsonOptions(sp.GetRequiredService<IOptions<CachingOptions>>().Value),
        });

        // The concrete serializer is registered separately so AddBrotliCompression can decorate it
        // without resolving IFusionCacheSerializer from inside its own factory.
        services.TryAddSingleton(sp => new FusionCacheSystemTextJsonSerializer(sp.GetRequiredService<CacheSerializationOptions>().Value));
        services.TryAddSingleton<IFusionCacheSerializer>(sp => sp.GetRequiredService<FusionCacheSystemTextJsonSerializer>());

        services
            .AddFusionCache()
            .TryWithRegisteredLogger()
            // A dedicated MemoryCache keeps L1SizeLimit from affecting other IMemoryCache users.
            .WithMemoryCache(sp => new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = sp.GetRequiredService<IOptions<CachingOptions>>().Value.L1SizeLimit,
            }))
            .WithRegisteredSerializer()
            .WithPostSetup((sp, cache) => ApplyDefaults(cache.DefaultEntryOptions, sp.GetRequiredService<IOptions<CachingOptions>>().Value));

        services.TryAddSingleton<ICacheService, FusionCacheService>();

        // One key provider serves both interfaces, so global and tenant keys share the validated
        // CachingOptions.ServiceName. Consumers may override either registration.
        services.TryAddSingleton<CacheKeyProvider>();
        services.TryAddSingleton<ICacheKeyProvider>(sp => sp.GetRequiredService<CacheKeyProvider>());
        services.TryAddSingleton<ITenantCacheKeyProvider>(sp => sp.GetRequiredService<CacheKeyProvider>());

        return new CachingBuilder(services);
    }

    // Every entry starts from these defaults; FusionCacheService copies them before applying a policy.
    internal static void ApplyDefaults(FusionCacheEntryOptions defaults, CachingOptions options)
    {
        // Size = 1 so every entry counts as one unit against L1SizeLimit.
        defaults.Size = 1;

        if (options.DistributedCacheSoftTimeout is { } soft)
            defaults.DistributedCacheSoftTimeout = soft;

        if (options.DistributedCacheHardTimeout is { } hard)
            defaults.DistributedCacheHardTimeout = hard;

        if (options.FailSafeThrottleDuration is { } throttle)
            defaults.FailSafeThrottleDuration = throttle;
    }

    private static JsonSerializerOptions CreateJsonOptions(CachingOptions options) =>
        options.SerializerContext is { } context
            ? new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine(context, EncryptedCacheEntryJsonContext.Default) }
            : new JsonSerializerOptions();
}
