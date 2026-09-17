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
    /// Uses the <see cref="ITenantCacheKeyProvider"/> that <c>AddSharedKernelCaching</c> registers.
    /// Idempotent: calling it more than once registers the service once.
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


        builder.Services.TryAddSingleton<ITenantCacheService, TenantCacheService>();

        return builder;
    }
}
