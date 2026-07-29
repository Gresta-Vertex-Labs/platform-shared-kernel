using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// DI convenience extensions registering the <see cref="SharedKernel.Testing.Caching"/> fakes.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeCacheService"/>, <see cref="FakeDistributedLockService"/>,
    /// <see cref="FakeTenantCacheKeyProvider"/>, <see cref="FakeCacheInvalidationBus"/>,
    /// <see cref="FakeRedisChannelService"/>, and <see cref="FakeRedisHashService"/> as
    /// singletons.
    /// </summary>
    /// <remarks>
    /// Supersedes the manual per-fake <c>AddSingleton&lt;T&gt;</c> pattern for callers who want all
    /// six fakes; that pattern remains valid for callers who want only a subset.
    /// <see cref="FakeTypedHashStore{T}"/> and <see cref="FakeCacheWarmupStrategy"/> are not
    /// bundled into this call — see <see cref="AddFakeTypedHashStore{T}"/> and
    /// <see cref="AddFakeCacheWarmupStrategy"/>.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeCachingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICacheService, FakeCacheService>();
        services.AddSingleton<IDistributedLockService, FakeDistributedLockService>();
        services.AddSingleton<ITenantCacheKeyProvider, FakeTenantCacheKeyProvider>();
        services.AddSingleton<ICacheInvalidationBus, FakeCacheInvalidationBus>();
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
    /// <typeparamref name="T"/> the test needs a typed hash store for.
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

    /// <summary>
    /// Registers a new <see cref="FakeCacheWarmupStrategy"/> as <see cref="ICacheWarmupStrategy"/>
    /// via <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors the real <c>AddCacheWarmup&lt;TStrategy&gt;()</c>'s own
    /// <c>TryAddEnumerable</c> multi-strategy registration shape exactly, so a test registering
    /// several named strategies observes the identical "multiple independently-registered
    /// strategies" composition production would. Call once per named strategy needed, passing the
    /// same <paramref name="executionLog"/> queue instance across calls to observe cross-strategy
    /// ordering.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="name">The fixed, human-readable strategy name.</param>
    /// <param name="order">The fixed execution order relative to other registered strategies.</param>
    /// <param name="executionLog">
    /// An optional shared queue the registered strategy appends <paramref name="name"/> to on every
    /// <see cref="ICacheWarmupStrategy.WarmupAsync"/> call.
    /// </param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeCacheWarmupStrategy(
        this IServiceCollection services,
        string name,
        int order = 0,
        ConcurrentQueue<string>? executionLog = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheWarmupStrategy>(
                _ => new FakeCacheWarmupStrategy(name, order, executionLog)));

        return services;
    }
}
