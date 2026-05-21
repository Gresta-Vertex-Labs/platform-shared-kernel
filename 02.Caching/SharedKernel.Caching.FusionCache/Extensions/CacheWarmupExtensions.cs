using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Extension methods on <see cref="ICachingBuilder"/> for registering cache warmup strategies.
/// </summary>
public static class CacheWarmupExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TStrategy"/> as an <see cref="ICacheWarmupStrategy"/>
    /// singleton and ensures that <see cref="CacheWarmupHostedService"/> is registered exactly
    /// once (idempotent — safe to call multiple times for different strategies).
    /// </summary>
    /// <typeparam name="TStrategy">
    /// A concrete <see cref="ICacheWarmupStrategy"/> implementation to register. Must have a
    /// public constructor whose parameters are resolvable from the DI container.
    /// </typeparam>
    /// <param name="builder">The caching builder returned by <c>AddSharedKernelCaching</c>.</param>
    /// <returns>The same <see cref="ICachingBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Multiple strategies can be registered by calling <c>AddCacheWarmup</c> repeatedly.
    /// They are executed in ascending <see cref="ICacheWarmupStrategy.Order"/> during startup.
    /// </para>
    /// <para>
    /// Set <c>CachingOptions.WaitForWarmup = true</c> to delay host readiness until all
    /// strategies complete. Example:
    /// <code>
    /// services.AddSharedKernelCaching(options => { options.WaitForWarmup = true; })
    ///         .AddCacheWarmup&lt;MyProductCatalogWarmup&gt;()
    ///         .AddCacheWarmup&lt;MyUserPreferencesWarmup&gt;();
    /// </code>
    /// </para>
    /// </remarks>
    public static ICachingBuilder AddCacheWarmup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
        this ICachingBuilder builder)
        where TStrategy : class, ICacheWarmupStrategy
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        // Register the strategy using TryAddEnumerable so multiple strategies of different
        // types can coexist and the same type is not registered twice.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheWarmupStrategy, TStrategy>());

        // Guard: register CacheWarmupHostedService exactly once, regardless of how many times
        // AddCacheWarmup<T> is called. TryAddEnumerable prevents duplicate hosted service
        // registrations for the same concrete type.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CacheWarmupHostedService>());

        return builder;
    }
}
