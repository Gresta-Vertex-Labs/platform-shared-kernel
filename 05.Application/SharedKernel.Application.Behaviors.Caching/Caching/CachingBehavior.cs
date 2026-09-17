using MediatR;
using SharedKernel.Application.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Wraps the inner pipeline in a cache lookup for queries implementing
/// <see cref="ICacheableQuery{TValue}"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The query type, constrained to <see cref="IQueryBase"/> and <see cref="ICacheableQuery"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline, <c>Result&lt;TValue&gt;</c>.</typeparam>
/// <remarks>
/// <para>
/// <b>Caches the value, not the <c>Result</c>.</b> A successful <c>Result&lt;TValue&gt;</c> is stored
/// as its <c>TValue</c> and rebuilt on a hit, so the entry round-trips through the distributed cache's
/// JSON serializer like any other value.
/// </para>
/// <para>
/// <b>Stampede-protected, never caches a failure.</b> The handler runs inside
/// <see cref="ICacheService.GetOrSetAsync{T}(string, Func{CacheFactoryContext, CancellationToken, ValueTask{T}}, CachePolicy, CancellationToken)"/>,
/// so concurrent identical queries do not all run it at once. A failed <c>Result</c> is returned to
/// the caller and skipped for caching through <see cref="CacheFactoryContext.SkipCaching"/>; a query
/// waiting on the same key then runs the handler itself.
/// </para>
/// <para>
/// <b>No background handler runs.</b> Eager refresh and factory timeouts would let the cache run the
/// handler after the request's DI scope has been disposed, so this behavior turns them off on the
/// query's policy. Fail-safe and every other setting are kept.
/// </para>
/// <para>
/// <b>Tenant scoping.</b> When <see cref="IRequestContext"/> is registered and has a
/// <see cref="IRequestContext.TenantId"/>, the key becomes <c>@{tenant}:{key}</c> and the policy is
/// scoped with <see cref="CachePolicy.ForTenant"/>, in the <see cref="CacheKeyFormat"/> tenant format.
/// A query can then never read, or be invalidated by, another tenant's entry, and
/// <c>ITenantCacheService.RemoveTenantAsync</c> also removes the tenant's query results. Without a
/// tenant the query's own key is used unchanged; it must not start with <c>@</c>.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(ICacheService cacheService, IRequestContext? requestContext = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase, ICacheableQuery, IRequest<TResponse>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The query's cache key is empty, or starts with <c>@</c> outside a tenant scope.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TResponse"/> is not the query's <c>Result&lt;TValue&gt;</c>.</exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var tenantId = requestContext?.TenantId;
        var key = CacheScope.Key(tenantId, request.CacheKey);
        var policy = CacheScope.Policy(tenantId, request.CachePolicy)
            .WithoutEagerRefresh()
            .WithFactoryTimeouts(softTimeout: null, hardTimeout: null);

        return await request.GetOrSetAsync(cacheService, key, policy, next, cancellationToken).ConfigureAwait(false);
    }
}
