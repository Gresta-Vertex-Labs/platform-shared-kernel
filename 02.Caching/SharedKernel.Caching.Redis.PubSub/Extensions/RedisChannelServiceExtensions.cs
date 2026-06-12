using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Polly;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.PubSub.Extensions;

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
    /// Requires that <c>IConnectionMultiplexer</c> is already registered in the service collection.
    /// Call <c>AddRedisConnection</c> (directly, or transitively via <c>AddRedisL2</c>,
    /// <c>AddRedisDistributedLocking</c>, or <c>AddRedisHashService</c>) first to satisfy this dependency.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="IConnectionMultiplexer"/> has not been registered. Call
    /// <c>AddRedisConnection</c> (directly, or transitively via <c>AddRedisL2</c>,
    /// <c>AddRedisDistributedLocking</c>, or <c>AddRedisHashService</c>) first.
    /// </exception>
    public static ICachingBuilder AddRedisChannelService(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Services.Any(sd => sd.ServiceType == typeof(IConnectionMultiplexer)))
        {
            throw new InvalidOperationException(
                "AddRedisChannelService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisHashService) to be called first to register IConnectionMultiplexer.");
        }

        // Use a factory registration so the optional ResiliencePipeline (circuit breaker)
        // is resolved from DI when present. When not registered (Enabled = false), the
        // pipeline parameter is null and RedisChannelService operates without Polly overhead.
        builder.Services.TryAddSingleton<IRedisChannelService>(sp =>
            new RedisChannelService(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<ILogger<RedisChannelService>>(),
                sp.GetService<ResiliencePipeline>()));

        return builder;
    }
}
