using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for registering the Redis Hash service.
/// </summary>
public static class RedisHashServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IRedisHashService"/> as a singleton backed by StackExchange.Redis hash commands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires that <c>IConnectionMultiplexer</c> is already registered in the service collection.
    /// Call <c>AddRedisL2</c> or <c>AddRedisDistributedLocking</c> first to satisfy this dependency.
    /// </para>
    /// <para>
    /// The service shares the existing <c>IConnectionMultiplexer</c> singleton — no additional Redis
    /// connections are created.
    /// </para>
    /// </remarks>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>IConnectionMultiplexer</c> has not been registered. Call
    /// <c>AddRedisDistributedLocking</c> or <c>AddRedisL2</c> first.
    /// </exception>
    public static ICachingBuilder AddRedisHashService(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Guard: IConnectionMultiplexer must already be registered.
        var multiplexerDescriptor = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));

        if (multiplexerDescriptor is null)
        {
            throw new InvalidOperationException(
                "AddRedisHashService requires AddRedisDistributedLocking or AddRedisL2 to be called first to register IConnectionMultiplexer.");
        }

        builder.Services.TryAddSingleton<IRedisHashService, RedisHashService>();

        return builder;
    }

    /// <summary>
    /// Registers <see cref="ITypedHashStore{T}"/> as a singleton, capturing
    /// <paramref name="typeInfo"/> once at registration time.
    /// </summary>
    /// <typeparam name="T">The DTO type to store in the Redis hash.</typeparam>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <param name="typeInfo">
    /// The STJ <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/>.
    /// Typically obtained from a source-generated context, e.g.
    /// <c>MyAppSerializerContext.Default.OrderDto</c>.
    /// </param>
    /// <returns>The same <paramref name="builder"/> to allow further chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="typeInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>IRedisHashService</c> has not been registered.
    /// Call <c>AddRedisHashService</c> before <c>AddTypedHashStore&lt;T&gt;</c>.
    /// </exception>
    public static ICachingBuilder AddTypedHashStore<T>(
        this ICachingBuilder builder,
        JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(typeInfo);

        // Guard: IRedisHashService must already be registered.
        var hashServiceDescriptor = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(IRedisHashService));

        if (hashServiceDescriptor is null)
        {
            throw new InvalidOperationException(
                "AddTypedHashStore<T> requires AddRedisHashService to be called first.");
        }

        // Register a singleton factory that captures typeInfo at registration time.
        builder.Services.AddSingleton<ITypedHashStore<T>>(
            sp => new TypedHashStore<T>(sp.GetRequiredService<IRedisHashService>(), typeInfo));

        return builder;
    }
}
