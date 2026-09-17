using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;

namespace SharedKernel.Caching.Redis.HashStore.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods that register the Redis hash store over the shared connection.
/// </summary>
public static class RedisHashServiceExtensions
{
    /// <summary>Registers <see cref="IRedisHashService"/> over the shared Redis connection.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><c>AddRedisConnection</c> has not been called.</exception>
    /// <remarks>Calling it more than once has no further effect.</remarks>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddRedisConnection(builder.Configuration)
    ///     .AddRedisHashService();
    /// </code>
    /// </example>
    public static IServiceCollection AddRedisHashService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.EnsureRedisConnectionRegistered(nameof(AddRedisHashService));

        services.TryAddSingleton<IRedisHashService, RedisHashService>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ITypedHashStore{T}"/> for <typeparamref name="T"/>, and <see cref="IRedisHashService"/>
    /// when it is not registered yet.
    /// </summary>
    /// <typeparam name="T">The type of every field value.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>, typically from a source-generated context.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddRedisConnection</c> has not been called, or a store for <typeparamref name="T"/> is already registered.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddRedisConnection(builder.Configuration)
    ///     .AddTypedHashStore(AppJsonContext.Default.SessionDto)
    ///     .AddTypedHashStore(AppJsonContext.Default.Int64);
    /// </code>
    /// </example>
    public static IServiceCollection AddTypedHashStore<T>(this IServiceCollection services, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(typeInfo);
        services.AddRedisHashService();

        if (services.Any(d => d.ServiceType == typeof(ITypedHashStore<T>)))
        {
            throw new InvalidOperationException(
                $"An ITypedHashStore<{typeof(T).Name}> is already registered; register one store per type.");
        }

        services.AddSingleton<ITypedHashStore<T>>(sp => new TypedHashStore<T>(sp.GetRequiredService<IRedisHashService>(), typeInfo));
        return services;
    }
}
