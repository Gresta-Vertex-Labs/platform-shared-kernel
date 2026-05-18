using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for the cross-service cache invalidation bus.
/// </summary>
public static class CacheInvalidationExtensions
{
    /// <summary>
    /// Registers <see cref="ICacheInvalidationBus"/> as a singleton backed by Redis Pub/Sub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IRedisChannelService"/> must already be registered in the service collection.
    /// Call <c>AddRedisChannelService()</c> before this method.
    /// </para>
    /// <para>
    /// This registration is opt-in and additive — it is not called automatically by
    /// <c>AddRedisL2</c> or <c>AddSharedKernelCaching</c>.
    /// </para>
    /// <para>
    /// <b>No delivery guarantees.</b> The invalidation bus uses Redis Pub/Sub (at-most-once
    /// delivery). For durable, ordered, or transactional delivery use <c>SharedKernel.Messaging</c>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="IRedisChannelService"/> has not been registered.
    /// Call <c>AddRedisChannelService()</c> first.
    /// </exception>
    public static ICachingBuilder AddRedisCacheInvalidationBus(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Guard: IRedisChannelService must be registered before the invalidation bus.
        if (!builder.Services.Any(sd => sd.ServiceType == typeof(IRedisChannelService)))
        {
            throw new InvalidOperationException(
                "AddRedisCacheInvalidationBus requires AddRedisChannelService to be called first.");
        }

        builder.Services.TryAddSingleton<ICacheInvalidationBus, RedisCacheInvalidationBus>();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="CacheInvalidationReceiver"/> as a hosted background service that
    /// subscribes to Redis invalidation channels and dispatches eviction calls to the local
    /// <see cref="ICacheService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The receiver subscribes to two channels on startup:
    /// <list type="bullet">
    ///   <item><description>Own-service targeted channel: <c>sharedkernel:cache:invalidation:{service-name}</c></description></item>
    ///   <item><description>Broadcast channel: <c>sharedkernel:cache:invalidation:broadcast</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// All deserialization and cache-service errors are caught, logged, and swallowed — the
    /// receiver never propagates exceptions to the host.
    /// </para>
    /// <para>
    /// This registration is opt-in and additive — it is not called automatically by
    /// <c>AddRedisL2</c> or <c>AddSharedKernelCaching</c>.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    public static ICachingBuilder AddCacheInvalidationReceiver(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHostedService<CacheInvalidationReceiver>();

        return builder;
    }
}
