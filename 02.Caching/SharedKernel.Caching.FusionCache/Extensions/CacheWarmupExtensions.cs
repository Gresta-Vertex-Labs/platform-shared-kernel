using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// <see cref="ICachingBuilder"/> extension methods for cache warmup at startup.
/// </summary>
public static class CacheWarmupExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TStrategy"/> to run once at startup and fill the cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strategies run in ascending <see cref="ICacheWarmupStrategy.Order"/>; a failing strategy is logged
    /// and the next one runs. Calling this for several strategy types registers each once.
    /// </para>
    /// <para>
    /// By default warmup runs in the background after startup. Set <see cref="CachingOptions.WaitForWarmup"/>
    /// to hold host startup, and therefore traffic and readiness, until every strategy has run.
    /// </para>
    /// </remarks>
    /// <typeparam name="TStrategy">The strategy type, resolved from the container as a singleton.</typeparam>
    /// <param name="builder">The caching builder.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// services.AddSharedKernelCaching(o =&gt; { o.ServiceName = "catalog"; o.WaitForWarmup = true; })
    ///         .AddCacheWarmup&lt;CurrencyWarmup&gt;()
    ///         .AddCacheWarmup&lt;CategoryTreeWarmup&gt;();
    /// </code>
    /// </example>
    public static ICachingBuilder AddCacheWarmup<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStrategy>(
        this ICachingBuilder builder)
        where TStrategy : class, ICacheWarmupStrategy
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheWarmupStrategy, TStrategy>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CacheWarmupHostedService>());

        return builder;
    }
}
