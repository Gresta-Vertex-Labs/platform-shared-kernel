using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Caching.Shared;
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
/// <b>Every key is namespaced by query type.</b> The key is built through the registered
/// <see cref="ITenantCacheKeyProvider"/> as <c>{service}:{QueryType}:{CacheKey}</c>, so two query
/// types that happen to choose the same <see cref="ICacheableQuery.CacheKey"/> cannot read each
/// other's entries. Without the namespace they would, and because the entry holds a bare JSON value,
/// the collision would surface as a partially-populated object rather than an error.
/// </para>
/// <para>
/// <b>Scope failures fail closed.</b> A <see cref="CacheScope.Tenant"/> query on a request with no
/// resolved tenant, or a <see cref="CacheScope.User"/> query with no authenticated caller, does not
/// fall back to a wider key: the cache is skipped, the handler runs, and the miss is logged at
/// Warning. A silent fallback would let two tenants whose resolution failed share one entry.
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
/// </remarks>
internal sealed partial class CachingBehavior<TRequest, TResponse>(
    ICacheService cacheService,
    ITenantCacheKeyProvider cacheKeys,
    CachingBehaviorsMetrics metrics,
    ILogger<CachingBehavior<TRequest, TResponse>> logger,
    IRequestContext? requestContext = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IQueryBase, ICacheableQuery, IRequest<TResponse>
{
    private static readonly string QueryTypeName = CacheEntityName.For(typeof(TRequest));

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The query's cache key is empty.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TResponse"/> is not the query's <c>Result&lt;TValue&gt;</c>.</exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var scope = request.Scope;

        if (!CacheKeyBuilder.TryBuild(
                cacheKeys,
                scope,
                QueryTypeName,
                request.CacheKey,
                requestContext?.TenantId,
                requestContext?.UserId,
                out var key))
        {
            LogScopeUnavailable(logger, QueryTypeName, scope);
            Record(scope, CacheOutcome.BypassedScopeUnavailable);
            return await next().ConfigureAwait(false);
        }

        var policy = CacheKeyBuilder.Policy(scope, request.CachePolicy, requestContext?.TenantId)
            .WithoutEagerRefresh()
            .WithFactoryTimeouts(softTimeout: null, hardTimeout: null);

        var execution = new CacheExecution();
        var response = await request
            .ExecuteAsync(cacheService, key, policy, execution, next, cancellationToken)
            .ConfigureAwait(false);

        Record(scope, execution.Outcome);
        return response;
    }

    private void Record(CacheScope scope, CacheOutcome outcome)
    {
        metrics.RecordQueryOutcome(QueryTypeName, scope, outcome);
        LogOutcome(logger, QueryTypeName, scope, outcome);

        // Annotates the request span the TracingBehavior already started rather than starting one of
        // its own: a cache lookup is a property of the request, not a separate unit of work, and a
        // new ActivitySource name would export nothing until a host registered it.
        Activity.Current?.SetTag("sharedkernel.cache.outcome", outcome.ToString());
        Activity.Current?.SetTag("sharedkernel.cache.scope", scope.ToString());
    }

    [LoggerMessage(
        EventId = CachingBehaviorsLoggingEventIds.LogQueryCacheOutcome,
        Level = LogLevel.Debug,
        Message = "Query {QueryType} cache outcome {CacheOutcome} at {CacheScope} scope")]
    private static partial void LogOutcome(ILogger logger, string queryType, CacheScope cacheScope, CacheOutcome cacheOutcome);

    [LoggerMessage(
        EventId = CachingBehaviorsLoggingEventIds.LogQueryScopeUnavailable,
        Level = LogLevel.Warning,
        Message = "Query {QueryType} declares {CacheScope} cache scope but that identity is unavailable on this request; the cache was skipped and the handler ran")]
    private static partial void LogScopeUnavailable(ILogger logger, string queryType, CacheScope cacheScope);
}
