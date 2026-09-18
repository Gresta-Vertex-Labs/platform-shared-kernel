using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching.Shared;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching.Extensions;

/// <summary>
/// Registers this package's behaviors into the shared <see cref="ApplicationBehaviorsBuilder"/>
/// pipeline.
/// </summary>
public static class CachingBehaviorsExtensions
{
    /// <summary>
    /// Opts in to the caching behavior (<see cref="PipelineStage.Query"/>) and the cache-invalidation
    /// behavior (<see cref="PipelineStage.Command"/>).
    /// </summary>
    /// <param name="builder">The builder to register against.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Both behaviors require <see cref="ICacheService"/> and <see cref="ITenantCacheKeyProvider"/>
    /// to be registered in the service collection — <c>Build()</c> throws
    /// <see cref="InvalidOperationException"/> naming the missing one otherwise, via the same generic
    /// <see cref="ApplicationBehaviorsBuilder.AddBehavior"/> required-service guard every other custom
    /// behavior uses. <c>AddSharedKernelCaching()</c> registers both.
    /// </para>
    /// <para>
    /// <see cref="ITenantCacheKeyProvider"/> is required rather than the narrower
    /// <see cref="ICacheKeyProvider"/> because <see cref="CacheScope.Tenant"/> is the default scope,
    /// so the tenant key form is needed on the ordinary path, not only when a service opts into
    /// multi-tenancy.
    /// </para>
    /// </remarks>
    public static ApplicationBehaviorsBuilder AddCachingBehaviors(this ApplicationBehaviorsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Mirrors how AddMetricsBehavior registers ApplicationMetrics: the instruments are a
        // singleton resolved from IMeterFactory, and AddMetrics guarantees one is resolvable.
        builder.Services.AddMetrics();
        builder.Services.TryAddSingleton<CachingBehaviorsMetrics>();

        return builder
            .AddBehavior(
                typeof(CachingBehavior<,>),
                PipelineStage.Query,
                typeof(ICacheService),
                typeof(ITenantCacheKeyProvider))
            .AddBehavior(
                typeof(CacheInvalidationBehavior<,>),
                PipelineStage.Command,
                typeof(ICacheService),
                typeof(ITenantCacheKeyProvider));
    }
}
