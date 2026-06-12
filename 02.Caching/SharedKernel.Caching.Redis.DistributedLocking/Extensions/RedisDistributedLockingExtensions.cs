using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.DistributedLocking.Extensions;

/// <summary>
/// Minimal <see cref="ICachingBuilder"/> implementation used internally in this package
/// to allow the <c>IServiceCollection</c> shim overload of <c>AddRedisDistributedLocking</c>
/// to delegate to the canonical <c>ICachingBuilder</c> extension without taking a dependency
/// on <c>SharedKernel.Caching.FusionCache</c> (sibling layering rule).
/// </summary>
internal sealed class RedisLockCachingBuilder(IServiceCollection services) : ICachingBuilder
{
    public IServiceCollection Services { get; } = services;
}

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for registering Redis-backed distributed locking
/// via RedLock.net.
/// </summary>
public static class RedisDistributedLockingExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> backed by RedLock.net over Redis.
    /// Also registers <see cref="IConnectionMultiplexer"/> as a singleton (via
    /// <see cref="RedisConnectionCoreExtensions.AddRedisConnection"/> — first caller wins) so
    /// that subsequent calls to <c>AddRedisChannelService</c> or <c>AddRedisHashService</c>
    /// share the same connection.
    /// </summary>
    /// <param name="builder">
    /// The caching builder returned by <c>AddSharedKernelCaching</c> or any other
    /// <c>ICachingBuilder</c> source.
    /// </param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisLockOptions"/>. When <see langword="null"/>
    /// the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="builder"/> to allow further fluent chaining.</returns>
    public static ICachingBuilder AddRedisDistributedLocking(
        this ICachingBuilder builder,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new RedisLockOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        var services = builder.Services;

        // Register the shared IConnectionMultiplexer singleton (and connection health tracker)
        // via SharedKernel.Caching.Redis.Core, if not already registered. TryAddSingleton
        // ensures AddRedisL2 and AddRedisDistributedLocking share the same connection —
        // first caller wins.
        services.AddRedisConnection(options.ConnectionString, coreOptions =>
        {
            coreOptions.ConnectTimeoutMs = options.ConnectTimeoutMs;
        });

        // Register RedLockFactory using the multiplexer — singleton lifetime.
        services.TryAddSingleton(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            var endPoints = new List<RedLockMultiplexer> { new(multiplexer) };
            return RedLockFactory.Create(endPoints);
        });

        // Expose IDistributedLockFactory (RedLockFactory implements it).
        services.TryAddSingleton<RedLockNet.IDistributedLockFactory>(
            sp => sp.GetRequiredService<RedLockFactory>());

        // Register the lock service implementation.
        services.TryAddSingleton<IDistributedLockService, RedLockDistributedLockService>();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> backed by RedLock.net over Redis.
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
    [Obsolete(
        "Use AddRedisDistributedLocking on ICachingBuilder instead. This overload will be removed in a future version.",
        error: false)]
    public static IServiceCollection AddRedisDistributedLocking(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        new RedisLockCachingBuilder(services)
            .AddRedisDistributedLocking(connectionString, configure);

        return services;
    }
}
