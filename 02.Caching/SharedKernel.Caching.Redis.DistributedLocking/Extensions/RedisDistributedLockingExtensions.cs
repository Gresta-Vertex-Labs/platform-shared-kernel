using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;

namespace SharedKernel.Caching.Redis.DistributedLocking.Extensions;

/// <summary>
/// Extension methods for registering Redis-backed distributed locks and leases.
/// </summary>
public static class RedisDistributedLockingExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> over Redis. Also registers the shared
    /// <c>IConnectionMultiplexer</c> (first caller wins), so other Redis packages reuse the same
    /// connection, and <see cref="TimeProvider.System"/> unless a <see cref="TimeProvider"/> is
    /// already registered.
    /// </summary>
    /// <param name="builder">The caching builder.</param>
    /// <param name="connectionString">A StackExchange.Redis connection string. Must not be null or whitespace.</param>
    /// <param name="configure">Optional delegate to customise <see cref="RedisLockOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ICachingBuilder AddRedisDistributedLocking(
        this ICachingBuilder builder,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Register(builder.Services, connectionString, configure);

        return builder;
    }

    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> over Redis for a host that needs locks and
    /// leases but no cache. Also registers the shared <c>IConnectionMultiplexer</c> (first caller
    /// wins), so other Redis packages reuse the same connection, and <see cref="TimeProvider.System"/>
    /// unless a <see cref="TimeProvider"/> is already registered.
    /// </summary>
    /// <remarks>
    /// Performs the same registration as the <see cref="ICachingBuilder"/> overload. Use that overload
    /// when the host also calls <c>AddSharedKernelCaching</c>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">A StackExchange.Redis connection string. Must not be null or whitespace.</param>
    /// <param name="configure">Optional delegate to customise <see cref="RedisLockOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRedisDistributedLocking(
        this IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Register(services, connectionString, configure);

        return services;
    }

    private static void Register(
        IServiceCollection services,
        string connectionString,
        Action<RedisLockOptions>? configure)
    {
        var options = new RedisLockOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        services.AddRedisConnection(options.ConnectionString, coreOptions =>
        {
            coreOptions.ConnectTimeoutMs = options.ConnectTimeoutMs;
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDistributedLockService, RedisDistributedLockService>();
    }
}
