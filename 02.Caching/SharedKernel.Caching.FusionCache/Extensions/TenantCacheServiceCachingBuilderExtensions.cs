using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Extension methods on <see cref="ICachingBuilder"/> for registering the tenant-scoped cache
/// service.
/// </summary>
public static class TenantCacheServiceCachingBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="TenantCacheService"/> as the <see cref="ITenantCacheService"/>
    /// singleton, wrapping <see cref="ICacheService"/> with mandatory, non-defaulted
    /// tenant-scoped key construction.
    /// </summary>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <see cref="ICachingBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This registration is <strong>additive</strong> to, and never replaces or removes, an
    /// already-registered <see cref="ITenantCacheKeyProvider"/>. If <c>AddTenantCacheKeyProvider()</c>
    /// has not already been called, this method also registers <see cref="ITenantCacheKeyProvider"/>
    /// (via <c>TryAddSingleton</c>, matching that method's own idempotency contract) so
    /// <see cref="ITenantCacheService"/> resolves correctly on its own — calling
    /// <c>AddTenantCacheKeyProvider()</c> first, or not at all, produces the identical resulting
    /// registration.
    /// </para>
    /// <para>
    /// Inject <see cref="ITenantCacheService"/> in any multi-tenant service as the recommended
    /// default entry point. <see cref="ICacheService"/> and <see cref="ITenantCacheKeyProvider"/>
    /// remain available and fully supported for non-tenant-scoped data and raw key construction
    /// respectively.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// services.AddSharedKernelCaching(options => { options.ServiceName = "my-service"; })
    ///         .AddTenantCacheService();
    /// // Inject: ITenantCacheService → GetOrSetAsync(tenantId, entity, id, factory, policy, ct)
    /// </code>
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddTenantCacheService(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Additive: registers ITenantCacheKeyProvider only if nothing has claimed it yet. A prior
        // standalone AddTenantCacheKeyProvider() call is left completely untouched.
        builder.Services.TryAddSingleton<ITenantCacheKeyProvider, TenantCacheKeyProvider>();

        builder.Services.TryAddSingleton<ITenantCacheService, TenantCacheService>();

        return builder;
    }
}
