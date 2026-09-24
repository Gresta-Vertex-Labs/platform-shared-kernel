using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Caching;

/// <summary>
/// Adds this package's behaviors to the pipeline chosen in <c>AddSharedKernelApplication</c>.
/// </summary>
public static class CachingPipelineExtensions
{
    /// <summary>
    /// Opts in to the caching behavior (<see cref="PipelineStage.Query"/>) for queries that implement
    /// <see cref="ICacheableQuery{TValue}"/>, and the cache-invalidation behavior
    /// (<see cref="PipelineStage.Command"/>) for commands that implement <see cref="IInvalidatesCache"/>.
    /// </summary>
    /// <param name="builder">The pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Both behaviors need <see cref="ICacheService"/> and <see cref="ITenantCacheKeyProvider"/>;
    /// like every other seam they are checked when the host starts, so they may be registered before
    /// or after <c>AddSharedKernelApplication</c>. <c>AddSharedKernelCaching()</c> registers both.
    /// </para>
    /// <para>
    /// <see cref="ITenantCacheKeyProvider"/> is required rather than the narrower
    /// <see cref="ICacheKeyProvider"/> because <see cref="CacheScope.Tenant"/> is the default scope,
    /// so the tenant key form is needed on the ordinary path, not only when a service opts into
    /// multi-tenancy.
    /// </para>
    /// </remarks>
    public static ApplicationPipelineBuilder WithCaching(this ApplicationPipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The instruments are a singleton resolved from IMeterFactory, which the registration call
        // always makes resolvable.
        builder.Services.TryAddSingleton<CachingBehaviorsMetrics>();

        return builder
            .WithBehavior(
                typeof(CachingBehavior<,>),
                PipelineStage.Query,
                typeof(ICacheService),
                typeof(ITenantCacheKeyProvider))
            .WithBehavior(
                typeof(CacheInvalidationBehavior<,>),
                PipelineStage.Command,
                typeof(ICacheService),
                typeof(ITenantCacheKeyProvider));
    }
}
