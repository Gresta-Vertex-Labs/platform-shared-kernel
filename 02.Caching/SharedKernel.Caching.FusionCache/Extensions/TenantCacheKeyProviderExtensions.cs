using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Extension methods on <see cref="ICachingBuilder"/> for registering the multi-tenant
/// cache key provider.
/// </summary>
public static class TenantCacheKeyProviderExtensions
{
    /// <summary>
    /// Registers <see cref="TenantCacheKeyProvider"/> as the <see cref="ITenantCacheKeyProvider"/>
    /// singleton for multi-tenant cache key namespacing.
    /// </summary>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <see cref="ICachingBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// This registration is <strong>additive</strong> — it does not replace the existing
    /// <see cref="ICacheKeyProvider"/> singleton. Both <see cref="ICacheKeyProvider"/> and
    /// <see cref="ITenantCacheKeyProvider"/> coexist in the DI container.
    /// </para>
    /// <para>
    /// Inject <see cref="ITenantCacheKeyProvider"/> wherever per-tenant key isolation is
    /// required. Continue injecting <see cref="ICacheKeyProvider"/> for standard (non-tenant)
    /// key construction. The two registrations are independent.
    /// </para>
    /// <para>
    /// <see cref="TenantCacheKeyProvider"/> reads the service name from
    /// <c>IOptions&lt;CachingCoreOptions&gt;</c>, which is registered by
    /// <c>AddSharedKernelCaching</c> or <c>AddCachingCoreOptions</c>. Always call one of
    /// those before calling this method.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// services.AddSharedKernelCaching(options => { options.ServiceName = "my-service"; })
    ///         .AddTenantCacheKeyProvider();
    /// // Inject: ITenantCacheKeyProvider → BuildTenantKey(tenantId, entity, id)
    /// </code>
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddTenantCacheKeyProvider(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Register TenantCacheKeyProvider as ITenantCacheKeyProvider singleton.
        // TryAddSingleton ensures idempotency — safe to call multiple times.
        // This does NOT touch the existing ICacheKeyProvider registration.
        builder.Services.TryAddSingleton<ITenantCacheKeyProvider, TenantCacheKeyProvider>();

        return builder;
    }
}
