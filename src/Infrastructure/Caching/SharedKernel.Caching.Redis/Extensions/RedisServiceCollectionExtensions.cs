using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Configuration.Extensions;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods that add Redis as the distributed layer and backplane of
/// the cache registered by <c>AddSharedKernelCaching</c>.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis as the distributed layer and backplane, with <see cref="RedisL2Options"/> set in code (or left
    /// at their defaults) and validated at startup.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <param name="configure">Optional changes to the defaults.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddRedisConnection</c> has not been called, or the distributed layer is already registered.
    /// </exception>
    /// <remarks>
    /// Entries are written to Redis as well as to memory, so every instance of the service shares them, and the
    /// backplane carries removals, expirations, tag evictions and clears to every instance. Both use the shared
    /// connection from <c>AddRedisConnection</c>, so its TLS settings and timeouts apply. Entries created with
    /// <see cref="CachePolicy.LocalOnly"/> stay in memory.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
    /// builder.Services.AddSharedKernelCaching(o => o.ServiceName = "orders").AddRedisL2();
    /// </code>
    /// </example>
    public static ICachingBuilder AddRedisL2(
        this ICachingBuilder builder,
        Action<RedisL2Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        EnsureCanRegister(services);

        var options = services.AddOptions<RedisL2Options>();
        if (configure is not null)
            options.Configure(configure);
        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RedisL2Options>, RedisL2OptionsValidator>());

        AddCore(services);
        return builder;
    }

    /// <summary>
    /// Adds Redis as the distributed layer and backplane, with <see cref="RedisL2Options"/> bound from the
    /// <c>SharedKernel:Caching:Redis:L2</c> section and validated at startup.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <param name="configuration">The configuration root that contains the section; the section is optional.</param>
    /// <param name="configure">Optional changes applied after binding.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddRedisConnection</c> has not been called, or the distributed layer is already registered.
    /// </exception>
    /// <remarks>See <see cref="AddRedisL2(ICachingBuilder, Action{RedisL2Options}?)"/>.</remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(builder.Configuration);
    /// builder.Services.AddSharedKernelCaching(builder.Configuration).AddRedisL2(builder.Configuration);
    /// </code>
    /// </example>
    public static ICachingBuilder AddRedisL2(
        this ICachingBuilder builder,
        IConfiguration configuration,
        Action<RedisL2Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        var services = builder.Services;
        EnsureCanRegister(services);

        services.AddValidatedOptions<RedisL2Options, RedisL2OptionsValidator>(configuration);
        if (configure is not null)
            services.Configure(configure);

        AddCore(services);
        return builder;
    }

    private static void EnsureCanRegister(IServiceCollection services)
    {
        services.EnsureRedisConnectionRegistered(nameof(AddRedisL2));

        if (services.Any(d => d.ServiceType == typeof(RedisL2Marker)))
            throw new InvalidOperationException("AddRedisL2 has already been called for this service collection.");
    }

    private static void AddCore(IServiceCollection services)
    {
        services.AddSingleton<RedisL2Marker>();

        services.AddOptions<FusionCacheOptions>()
            .Configure<IOptions<RedisL2Options>>((cache, l2) =>
            {
                cache.DistributedCacheCircuitBreakerDuration = l2.Value.DistributedCacheCircuitBreakerDuration;
                cache.BackplaneCircuitBreakerDuration = l2.Value.BackplaneCircuitBreakerDuration;

                // A key prefix separates deployments sharing one Redis; their backplane notifications are separated too.
                if (!string.IsNullOrEmpty(l2.Value.KeyPrefix))
                    cache.BackplaneChannelPrefix = l2.Value.KeyPrefix;
            });

        // WithRegisteredSerializer keeps the serializer AddSharedKernelCaching registered, including its
        // SerializerContext and any Brotli or encryption decoration; never replace it with a default one here.
        services
            .AddFusionCache()
            .WithRegisteredOptions()
            // Handed to FusionCache only, never registered as IDistributedCache: it runs on the shared connection
            // and nothing else may dispose it.
            .WithDistributedCache(sp => new RedisDistributedCache(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<IOptions<RedisL2Options>>().Value.KeyPrefix,
                // FusionCache computes absolute expirations from the system clock, so the TTL must use the same clock.
                TimeProvider.System))
            .WithRegisteredSerializer()
            // The backplane reuses the shared connection through a wrapper that ignores Close and Dispose, because
            // the FusionCache backplane disposes the connection it is given when it unsubscribes.
            .WithBackplane(sp => new RedisBackplane(
                Options.Create(new RedisBackplaneOptions
                {
                    ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(
                        new SharedConnectionMultiplexer(sp.GetRequiredService<IConnectionMultiplexer>())),
                }),
                sp.GetRequiredService<ILogger<RedisBackplane>>()));
    }

    // Marks that AddRedisL2 has run, so a second call fails instead of stacking registrations.
    private sealed class RedisL2Marker;
}
