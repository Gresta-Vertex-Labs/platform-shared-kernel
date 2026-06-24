using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// DI convenience extension registering every <see cref="SharedKernel.Testing.Caching"/> fake
/// in a single call.
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FakeCacheService"/>, <see cref="FakeDistributedLockService"/>,
    /// <see cref="FakeTenantCacheKeyProvider"/>, and <see cref="FakeCacheInvalidationBus"/> as
    /// singletons.
    /// </summary>
    /// <remarks>
    /// Supersedes the manual per-fake <c>AddSingleton&lt;T&gt;</c> pattern for callers who want all
    /// four fakes; that pattern remains valid for callers who want only a subset.
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

        return services;
    }
}
