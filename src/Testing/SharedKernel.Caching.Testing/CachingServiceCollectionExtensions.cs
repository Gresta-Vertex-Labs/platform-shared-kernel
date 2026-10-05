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
    /// <see cref="FakeTenantCacheKeyProvider"/> (as both <see cref="ICacheKeyProvider"/> and
    /// <see cref="ITenantCacheKeyProvider"/>) as singletons.
    /// </summary>
    /// <remarks>
    /// Supersedes the manual per-fake <c>AddSingleton&lt;T&gt;</c> pattern for callers who want all
    /// of these fakes; that pattern remains valid for callers who want only a subset. When a
    /// <see cref="TimeProvider"/> is registered, <see cref="FakeDistributedLockService"/> uses it for lease
    /// expiry; otherwise it uses <see cref="TimeProvider.System"/>. Resolve the interface and cast to the fake
    /// type to reach its assertion helpers. <see cref="FakeCacheWarmupStrategy"/> is not bundled into this
    /// call — see <see cref="AddFakeCacheWarmupStrategy"/>. The Redis-specific fakes (hash store, pub/sub)
    /// live in <c>SharedKernel.Caching.Redis.Testing</c> (<c>AddFakeRedisServices()</c>), so this package
    /// depends on <c>SharedKernel.Caching.Abstractions</c> only.
    /// </remarks>
    /// <param name="services">The service collection to register against.</param>
    /// <returns><paramref name="services"/>, for fluent chaining.</returns>
    public static IServiceCollection AddFakeCachingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICacheService, FakeCacheService>();
        services.AddSingleton<IDistributedLockService, FakeDistributedLockService>();
        services.AddSingleton<FakeTenantCacheKeyProvider>();
        services.AddSingleton<ICacheKeyProvider>(sp => sp.GetRequiredService<FakeTenantCacheKeyProvider>());
        services.AddSingleton<ITenantCacheKeyProvider>(sp => sp.GetRequiredService<FakeTenantCacheKeyProvider>());

        return services;
    }

    /// <summary>
    /// Registers <see cref="FakeTenantCacheService"/> as <see cref="ITenantCacheService"/>, singleton.
    /// </summary>
    /// <remarks>
    /// A standalone call, deliberately not bundled into <see cref="AddFakeCachingServices"/> —
    /// mirroring <see cref="AddFakeCacheWarmupStrategy"/>'s
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
    /// Deliberately uses plain <c>IServiceCollection.Add(ServiceDescriptor)</c> (via
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
