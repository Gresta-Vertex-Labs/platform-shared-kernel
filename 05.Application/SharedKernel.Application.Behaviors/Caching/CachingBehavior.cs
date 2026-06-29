using MediatR;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Wraps the inner pipeline in a stampede-protected cache lookup for queries implementing
/// <see cref="ICacheableQuery{TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">The query type, constrained to <see cref="ICacheableQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Calls <see cref="ICacheService.GetOrSetAsync{T}"/> — never <c>GetAsync</c> followed by
/// <c>SetAsync</c> — so FusionCache's stampede protection guarantees the factory runs exactly once
/// per key under concurrent load. <see cref="ICacheService.GetOrSetAsync{T}"/>'s factory parameter
/// is <see cref="Func{CancellationToken, ValueTask}"/> while MediatR's <c>next()</c> returns
/// <see cref="Task{TResponse}"/> — the behavior adapts via <c>cacheCt =&gt; new ValueTask&lt;TResponse&gt;(next())</c>,
/// never blocking with <c>.Result</c>/<c>.Wait()</c>. Must be registered after
/// <c>ValidationBehavior</c> in the pipeline — validation must reject before a cache lookup
/// occurs, so invalid requests are never cached.
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(ICacheService cacheService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICacheableQuery<TResponse>
{
    /// <inheritdoc/>
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return cacheService
            .GetOrSetAsync(
                request.CacheKey,
                _ => new ValueTask<TResponse>(next()),
                request.CachePolicy,
                cancellationToken)
            .AsTask();
    }
}
