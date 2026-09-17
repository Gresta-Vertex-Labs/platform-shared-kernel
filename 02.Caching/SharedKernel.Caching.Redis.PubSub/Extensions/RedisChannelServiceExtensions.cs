using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;

namespace SharedKernel.Caching.Redis.PubSub.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods that register Redis Pub/Sub over the shared connection.
/// </summary>
public static class RedisChannelServiceExtensions
{
    /// <summary>Registers <see cref="IRedisChannelService"/> over the shared Redis connection.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddRedisConnection</c> has not been called.</exception>
    /// <remarks>Calling it more than once has no further effect.</remarks>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddRedisConnection(builder.Configuration)
    ///     .AddRedisChannelService();
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisChannelService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.EnsureRedisConnectionRegistered(nameof(AddRedisChannelService));

        services.TryAddSingleton<IRedisChannelService, RedisChannelService>();
        return services;
    }
}
