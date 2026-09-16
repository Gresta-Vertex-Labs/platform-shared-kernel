using MediatR;
using SharedKernel.Application.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Wraps the inner pipeline in a cache lookup for queries implementing
/// <see cref="ICacheableQuery{TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The query type, constrained to <see cref="IQueryBase"/> and <see cref="ICacheableQuery{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// <b>Never caches a failure.</b> <see cref="ICacheService"/> has no "cache only if this predicate
/// holds" hook on its stampede-protected <c>GetOrSetAsync</c> — that method caches whatever the
/// factory returns, unconditionally. Since <typeparamref name="TResponse"/> here is the whole
/// wrapped <c>Result</c>/<c>Result&lt;T&gt;</c>, using <c>GetOrSetAsync</c> directly would cache a
/// <c>Result.Failure</c> exactly as readily as a success. This behavior therefore uses the explicit
/// <c>GetAsync</c>/<c>SetAsync</c> pair instead: on a miss, <c>next()</c> runs, and only a
/// successful response is stored. This is a deliberate, documented trade — it gives up
/// <c>GetOrSetAsync</c>'s built-in concurrent-request stampede protection in exchange for the
/// never-cache-a-failure guarantee, which matters more for a query result than de-duplicating a
/// handful of concurrent cache-miss executions.
/// </para>
/// <para>
/// <b>Tenant scoping.</b> When <see cref="IRequestContext"/> is registered and its
/// <see cref="IRequestContext.TenantId"/> is non-null, the cache key is prefixed as
/// <c>tenant:{tenantId}:{request.CacheKey}</c>, and every tag on <see cref="ICacheableQuery{TResponse}.CachePolicy"/>
/// is rewritten the same way before the entry is written — so a query can never read (or, via tag
/// eviction, be invalidated by) another tenant's entry. <see cref="IRequestContext"/> is resolved as
/// an optional dependency: a service that never registers it, or registers it but the current
/// request has no tenant, falls back to the unscoped key exactly as before this capability existed.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(ICacheService cacheService, IRequestContext? requestContext = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase, ICacheableQuery<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var key = ScopedKey(request.CacheKey);

        var cached = await cacheService.GetAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var response = await next().ConfigureAwait(false);

        if (response is not IHasSuccessFlag { IsSuccess: false })
        {
            var policy = ScopedPolicy(request.CachePolicy);
            await cacheService.SetAsync(key, response, policy, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private string ScopedKey(string cacheKey)
    {
        var tenantId = requestContext?.TenantId;
        return tenantId is null ? cacheKey : $"tenant:{tenantId}:{cacheKey}";
    }

    private CachePolicy ScopedPolicy(CachePolicy policy)
    {
        var tenantId = requestContext?.TenantId;
        if (tenantId is null || policy.Tags.Length == 0)
            return policy;

        return policy.WithTags(policy.Tags.Select(tag => $"tenant:{tenantId}:{tag}").ToArray());
    }
}
