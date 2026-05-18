using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for registering the Redis Pub/Sub channel service.
/// </summary>
public static class RedisChannelServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IRedisChannelService"/> as a singleton backed by StackExchange.Redis Pub/Sub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IRedisChannelService"/> is scoped to cache-adjacent ephemeral signaling only.
    /// For durable, ordered, or guaranteed-delivery messaging use <c>SharedKernel.Messaging</c> instead.
    /// </para>
    /// <para>
    /// This method requires that an <c>IConnectionMultiplexer</c> singleton is already registered in
    /// the service collection. Call <c>AddRedisL2</c> or <c>AddRedisDistributedLocking</c> first, or
    /// register the multiplexer manually.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    public static ICachingBuilder AddRedisChannelService(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IRedisChannelService, RedisChannelService>();

        return builder;
    }
}
