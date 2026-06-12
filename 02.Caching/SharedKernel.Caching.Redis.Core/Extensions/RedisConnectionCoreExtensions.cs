using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Core.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering the shared Redis
/// <see cref="IConnectionMultiplexer"/> and connection health tracking.
/// </summary>
public static class RedisConnectionCoreExtensions
{
    /// <summary>
    /// Registers the shared <see cref="IConnectionMultiplexer"/> singleton and
    /// <see cref="RedisConnectionHealthTracker"/> for the supplied connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// A StackExchange.Redis connection string (e.g., <c>"localhost:6379"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="RedisConnectionOptions"/>. When
    /// <see langword="null"/> the defaults are used.
    /// </param>
    /// <returns>The same <paramref name="services"/> to allow further chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is the single registration point for <see cref="IConnectionMultiplexer"/> across
    /// <c>SharedKernel.Caching.Redis</c>, <c>SharedKernel.Caching.Redis.DistributedLocking</c>,
    /// <c>SharedKernel.Caching.Redis.HashStore</c>, and <c>SharedKernel.Caching.Redis.PubSub</c>.
    /// </para>
    /// <para>
    /// The multiplexer is registered via <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, Func{IServiceProvider, TService})"/> —
    /// first caller wins. Calling this method multiple times across different <c>Add*</c>
    /// extensions in the same container is safe and idempotent; the connection string and
    /// options supplied by the first caller take effect.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisConnection(
        this IServiceCollection services,
        string connectionString,
        Action<RedisConnectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new RedisConnectionOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        var configOptions = ConfigurationOptions.Parse(options.ConnectionString);
        configOptions.ConnectTimeout = options.ConnectTimeoutMs;
        configOptions.AbortOnConnectFail = false;

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(configOptions));

        services.TryAddSingleton<RedisConnectionHealthTracker>();

        return services;
    }
}
