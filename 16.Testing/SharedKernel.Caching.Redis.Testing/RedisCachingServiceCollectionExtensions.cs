using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Caching.Redis.PubSub;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// DI convenience extensions registering the Redis-specific <see cref="SharedKernel.Testing.Caching"/> fakes.
/// </summary>
public static class RedisCachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeRedisChannelService"/> as <see cref="IRedisChannelService"/> and
    /// <see cref="FakeRedisHashService"/> as <see cref="IRedisHashService"/>, singletons.
    /// </summary>
    /// <remarks>
    /// Unlike the real Redis registrations, neither needs <c>AddRedisConnection</c>. When a
    /// <see cref="TimeProvider"/> is registered, <see cref="FakeRedisHashService"/> uses it for hash expiry;
    /// otherwise it uses <see cref="TimeProvider.System"/>. Combine with <c>AddFakeCachingServices()</c>
    /// (<c>SharedKernel.Caching.Testing</c>) for the provider-neutral cache and lock fakes.
    /// <see cref="FakeTypedHashStore{T}"/> is registered per type with <see cref="AddFakeTypedHashStore{T}"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeRedisServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRedisChannelService, FakeRedisChannelService>();
        services.AddSingleton<IRedisHashService, FakeRedisHashService>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="FakeTypedHashStore{T}"/> as <see cref="ITypedHashStore{T}"/>, singleton.
    /// </summary>
    /// <remarks>
    /// Matches the real <c>AddTypedHashStore&lt;T&gt;(JsonTypeInfo&lt;T&gt;)</c>'s own per-DTO-type
    /// registration shape (minus the type-info argument, which the fake never needs). Call once per
    /// <typeparamref name="T"/> the test needs a typed hash store for. Uses a registered
    /// <see cref="TimeProvider"/> for expiry when one exists.
    /// </remarks>
    /// <typeparam name="T">The DTO type stored in the fake hash.</typeparam>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeTypedHashStore<T>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITypedHashStore<T>, FakeTypedHashStore<T>>();

        return services;
    }
}
