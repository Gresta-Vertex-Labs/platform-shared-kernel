using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;

namespace SharedKernel.Caching.Redis.DistributedLocking.Extensions;

/// <summary>
/// Extension methods that register <see cref="IDistributedLockService"/> over the shared Redis connection.
/// </summary>
public static class RedisDistributedLockingExtensions
{
    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> over the shared Redis connection, for a host that also
    /// uses the cache.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddRedisConnection</c> has not been called.</exception>
    /// <remarks>
    /// Also registers <see cref="TimeProvider.System"/> unless a <see cref="TimeProvider"/> is already registered.
    /// Calling it more than once has no further effect.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(builder.Configuration);
    /// builder.Services.AddSharedKernelCaching(builder.Configuration)
    ///     .AddRedisL2()
    ///     .AddRedisDistributedLocking();
    /// </code>
    /// </example>
    public static ICachingBuilder AddRedisDistributedLocking(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        Register(builder.Services);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="IDistributedLockService"/> over the shared Redis connection, for a host that needs
    /// locks and leases but no cache.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddRedisConnection</c> has not been called.</exception>
    /// <remarks>
    /// Also registers <see cref="TimeProvider.System"/> unless a <see cref="TimeProvider"/> is already registered.
    /// Calling it more than once has no further effect.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddRedisConnection(builder.Configuration)
    ///     .AddRedisDistributedLocking();
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisDistributedLocking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Register(services);
        return services;
    }

    private static void Register(IServiceCollection services)
    {
        services.EnsureRedisConnectionRegistered(nameof(AddRedisDistributedLocking));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDistributedLockService, RedisDistributedLockService>();
    }
}
