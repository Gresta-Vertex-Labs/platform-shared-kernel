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
    /// Registers <see cref="FakeTenantCacheService"/> as <see cref="ITenantCacheService"/>, singleton.
    /// </summary>
    /// <remarks>
    /// A standalone call, deliberately not bundled into <see cref="AddFakeCachingServices"/> —
    /// mirroring <see cref="AddFakeTypedHashStore{T}"/>/<see cref="AddFakeCacheWarmupStrategy"/>'s
    /// existing standalone-registration precedent. This matches the real, production
    /// <c>AddTenantCacheService(this ICachingBuilder)</c> extension's own documented relationship
    /// to <c>AddTenantCacheKeyProvider()</c>: additive, never a replacement.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeTenantCacheService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITenantCacheService, FakeTenantCacheService>();

        return services;
    }

    /// <summary>
    /// Registers a new <see cref="FakeCacheWarmupStrategy"/> as <see cref="ICacheWarmupStrategy"/>,
    /// singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately uses plain <see cref="IServiceCollection.Add(ServiceDescriptor)"/> (via
    /// <c>AddSingleton</c>), never
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// — this is a corrected deviation from an earlier design draft that specified
    /// <c>TryAddEnumerable</c> to "mirror" the real <c>AddCacheWarmup&lt;TStrategy&gt;()</c>. That
    /// mirroring does not actually hold: the real extension is parameterized by a distinct generic
    /// <c>TStrategy</c> per call, so <c>TryAddEnumerable</c>'s de-duplication-by-implementation-type
    /// genuinely prevents registering the same concrete strategy type twice. This fake extension is
    /// parameterized by a runtime <paramref name="name"/> string instead — every call constructs the
    /// same concrete <see cref="FakeCacheWarmupStrategy"/> type regardless of the name/order/queue
    /// arguments, so <c>TryAddEnumerable</c> would treat every call after the first as an
    /// indistinguishable duplicate and silently drop it (confirmed at Tests-phase implementation
    /// time: a second/third call never appeared in the resolved <see cref="IEnumerable{T}"/> of
    /// <see cref="ICacheWarmupStrategy"/>). Plain <c>AddSingleton</c> has no such de-duplication and
    /// correctly registers one independent strategy instance per call.
    /// </para>
    /// <para>
    /// Call once per named strategy needed, passing the same <paramref name="executionLog"/> queue
    /// instance across calls to observe cross-strategy ordering.
    /// </para>
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

        services.AddSingleton<ICacheWarmupStrategy>(_ => new FakeCacheWarmupStrategy(name, order, executionLog));

        return services;
    }
}
