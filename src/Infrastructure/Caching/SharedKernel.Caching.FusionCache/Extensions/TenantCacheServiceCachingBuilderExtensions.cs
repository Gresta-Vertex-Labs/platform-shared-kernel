using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Implementations;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for the tenant-isolated cache.
/// </summary>
public static class TenantCacheServiceCachingBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="ITenantCacheService"/> over the registered <see cref="ICacheService"/>, with
    /// keys from the registered <see cref="ITenantCacheKeyProvider"/>.
    /// </summary>
    /// <remarks>
    /// It composes with <c>AddCacheEncryption</c>: tenant entries are then encrypted with the tenant key
    /// as associated data. Calling it more than once registers the service once.
    /// </remarks>
    /// <param name="builder">The caching builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCaching(o =&gt; o.ServiceName = "invoicing")
    ///         .AddTenantCacheService();
    /// </code>
    /// </example>
    public static ICachingBuilder AddTenantCacheService(this ICachingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ITenantCacheService, TenantCacheService>();

        return builder;
    }
}
