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
/// <b>Stampede-protected, never caches a failure.</b> The handler runs inside
/// <see cref="ICacheService.GetOrSetAsync{T}(string, Func{CacheFactoryContext, CancellationToken, ValueTask{T}}, CachePolicy, CancellationToken)"/>,
/// so concurrent identical queries run it once. A failed <c>Result</c> is returned to every waiting
/// caller but skipped for caching through <see cref="CacheFactoryContext.SkipCaching"/>.
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
    where TRequest : IQueryBase, ICacheableQuery<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The query's cache key is empty, or starts with <c>@</c> outside a tenant scope.</exception>
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

        return await cacheService.GetOrSetAsync<TResponse>(
            key,
            async (context, _) =>
            {
                var response = await next().ConfigureAwait(false);
                if (response is IHasSuccessFlag { IsSuccess: false })
                    context.SkipCaching();

                return response;
            },
            policy,
            cancellationToken).ConfigureAwait(false);
    }
}
