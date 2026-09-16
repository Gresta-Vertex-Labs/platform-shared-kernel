using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
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
    /// Opts in to <see cref="CachingBehavior{TRequest,TResponse}"/> (<see cref="PipelineStage.Query"/>)
    /// and <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/> (<see cref="PipelineStage.Command"/>).
    /// </summary>
    /// <param name="builder">The builder to register against.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Both behaviors require <see cref="ICacheService"/> to be registered in the service
    /// collection — <c>Build()</c> throws <see cref="InvalidOperationException"/> naming it
    /// otherwise, via the same generic <see cref="ApplicationBehaviorsBuilder.AddBehavior"/>
    /// required-service guard every other custom behavior uses.
    /// </remarks>
    public static ApplicationBehaviorsBuilder AddCachingBehaviors(this ApplicationBehaviorsBuilder builder)
        => builder
            .AddBehavior(typeof(CachingBehavior<,>), PipelineStage.Query, typeof(ICacheService))
            .AddBehavior(typeof(CacheInvalidationBehavior<,>), PipelineStage.Command, typeof(ICacheService));
}
