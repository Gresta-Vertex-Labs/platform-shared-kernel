using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using SharedKernel.Caching.Extensions;
using SharedKernel.Caching.Redis.Abstractions;
using SharedKernel.Caching.Redis.Implementations;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering Redis-backed caching
/// infrastructure: L2 distributed backplane and distributed locking via RedLock.net.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Redis L2 distributed backplane to the FusionCache instance registered by
    /// <c>AddSharedKernelCaching</c>. Must be called <b>after</b> <c>AddSharedKernelCaching</c>.
    /// </summary>
    /// <param name="builder">
    /// The caching builder returned by <c>AddSharedKernelCaching</c>.
    /// </param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisL2Options"/>. When <see langword="null"/>
    /// the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    public static ICachingBuilder AddRedisL2(
        this ICachingBuilder builder,
        string connectionString,
        Action<RedisL2Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new RedisL2Options { ConnectionString = connectionString };
        configure?.Invoke(options);

        var services = builder.Services;

        // Register the Redis IDistributedCache for FusionCache L2 storage.
        services.AddStackExchangeRedisCache(redisOptions =>
        {
            redisOptions.Configuration = options.ConnectionString;
            if (!string.IsNullOrEmpty(options.KeyPrefix))
                redisOptions.InstanceName = options.KeyPrefix;
        });

        // Wire the registered IDistributedCache into FusionCache as L2 +
        // register the backplane for cross-node invalidation notifications.
        // The STJ serializer must be re-registered because the distributed cache
        // requires it for L2 serialization.
        services
            .AddFusionCache()
            .WithRegisteredDistributedCache()
            .WithSystemTextJsonSerializer()
            .WithStackExchangeRedisBackplane(backplaneOptions =>
            {
                backplaneOptions.Configuration = options.ConnectionString;
            });

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IDistributedLockService"/>
    /// backed by RedLock.net over Redis. This registration is independent of
    /// <c>AddSharedKernelCaching</c> / <c>AddRedisL2</c> and can be called on its own.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisLockOptions"/>. When <see langword="null"/>
    /// the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> to allow further chaining.</returns>
    public static IServiceCollection AddRedisDistributedLocking(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new RedisLockOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        // Parse the connection string into ConfigurationOptions to safely set all parameters.
        var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configOptions.ConnectTimeout = options.ConnectTimeoutMs;
        configOptions.AbortOnConnectFail = false;

        // Register the IConnectionMultiplexer as a singleton for RedLock.
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configOptions));

        // Register RedLockFactory using the multiplexer — singleton lifetime.
        services.TryAddSingleton(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            var endPoints = new List<RedLockMultiplexer> { new(multiplexer) };
            return RedLockFactory.Create(endPoints);
        });

        // Expose IDistributedLockFactory (the RedLockFactory implements it).
        services.TryAddSingleton<RedLockNet.IDistributedLockFactory>(
            sp => sp.GetRequiredService<RedLockFactory>());

        // Register the service implementation.
        services.TryAddSingleton<IDistributedLockService, RedLockDistributedLockService>();

        return services;
    }
}
