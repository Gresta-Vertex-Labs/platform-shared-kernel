using SharedKernel.Application.Caching;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Caching;
using System.Diagnostics;
using SharedKernel.Application.Pipeline.Caching.Diagnostics;
using SharedKernel.Application.Pipeline.Caching.Shared;

using SharedKernel.Execution.Context;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>
/// Evicts the cache entries and tags declared by an <see cref="IInvalidatesCache"/> command after the
/// outermost command in the current DI scope commits.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IInvalidatesCache"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// On a successful response, registers an <see cref="ICommandScope.OnCompleted"/> callback that
/// performs the eviction — it does <b>not</b> evict directly from its own post-<c>next()</c> code.
/// This is how "evict only after a confirmed commit" is achieved without any pipeline-registration-
/// order trickery: <c>ICommandScope</c>'s post-<c>next()</c> code (running queued callbacks) always
/// executes last among the command-stage behaviors, strictly after
/// <c>SharedKernel.Application.Pipeline.Transaction.TransactionBehavior</c>'s commit has already
/// completed, regardless of where this behavior itself sits in the registration order. A
/// <c>Result.Failure</c> or a thrown exception registers nothing — there is nothing to evict.
/// </para>
/// <para>
/// <b>Eviction is not cancellable by the caller.</b> The callback runs with
/// <see cref="CancellationToken.None"/>, not the request token. The write has already committed by
/// then, so honouring a cancelled request token would abandon the eviction and leave the cache
/// serving a superseded value for the rest of the entry's lifetime — a client disconnecting at the
/// wrong moment must not be able to cause that.
/// </para>
/// <para>
/// <b>One failure does not abandon the rest.</b> Each entry and tag is evicted under its own
/// try/catch and logged at Error, so a single unreachable key cannot silently skip the remaining
/// evictions the command declared.
/// </para>
/// <para>
/// Scoping mirrors the caching behavior exactly, through the same key builder, so the key this
/// removes is the key that behavior wrote. A command whose declared <see cref="IInvalidatesCache.Scope"/>
/// has no identity on this request evicts nothing and logs at Warning, rather than evicting a
/// wider-scoped key it never wrote.
/// </para>
/// </remarks>
internal sealed class CacheInvalidationBehavior<TRequest, TResponse>(
    ICacheService cacheService,
    ITenantCacheKeyProvider cacheKeys,
    ICommandScope commandScope,
    CachingBehaviorsMetrics metrics,
    ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
    IRequestContext? requestContext = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IInvalidatesCache, IRequest<TResponse>
{
    private static readonly string CommandTypeName = CacheEntityName.For(typeof(TRequest));

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Scope and validate before the handler runs: an invalid target must fail the command, not
        // the post-commit callback after the change is already saved.
        var scope = request.Scope;
        var tenantId = requestContext?.TenantId;
        var userId = requestContext?.UserId;

        var keys = new List<string>(request.CacheKeysToInvalidate.Count);
        var scopeUnavailable = false;

        foreach (var entry in request.CacheKeysToInvalidate)
        {
            if (entry.QueryType is null)
            {
                throw new InvalidOperationException(
                    $"The command '{CommandTypeName}' declared a default CacheKeyRef. Build every entry with CacheKeyRef.For<TQuery>(key).");
            }

            if (CacheKeyBuilder.TryBuild(
                    cacheKeys, scope, CacheEntityName.For(entry.QueryType), entry.Key, tenantId, userId, out var key))
            {
                keys.Add(key);
            }
            else
            {
                scopeUnavailable = true;
            }
        }

        var tags = new List<string>(request.CacheTagsToInvalidate.Count);
        foreach (var tag in request.CacheTagsToInvalidate)
        {
            if (CacheKeyBuilder.TryBuildTag(scope, tag, tenantId, userId, out var scopedTag))
                tags.Add(scopedTag);
            else
                scopeUnavailable = true;
        }

        if (scopeUnavailable)
            PipelineCachingLog.InvalidationScopeUnavailable(logger, CommandTypeName, CacheTelemetryNames.Of(scope));

        var response = await next().ConfigureAwait(false);

        if (response is IHasSuccessFlag { IsSuccess: false })
            return response;

        if (keys.Count == 0 && tags.Count == 0)
            return response;

        commandScope.OnCompleted(async _ =>
        {
            foreach (var key in keys)
                await EvictAsync(key, isTag: false).ConfigureAwait(false);

            foreach (var tag in tags)
                await EvictAsync(tag, isTag: true).ConfigureAwait(false);

            PipelineCachingLog.InvalidationCompleted(logger, CommandTypeName, keys.Count, tags.Count);
        });

        return response;
    }

    private async Task EvictAsync(string target, bool isTag)
    {
        try
        {
            // CancellationToken.None, never the request token: see the class remarks.
            if (isTag)
                await cacheService.RemoveByTagAsync(target, CancellationToken.None).ConfigureAwait(false);
            else
                await cacheService.RemoveAsync(target, CancellationToken.None).ConfigureAwait(false);

            metrics.RecordEviction(CommandTypeName, CacheTelemetryNames.Target(isTag), succeeded: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.RecordEviction(CommandTypeName, CacheTelemetryNames.Target(isTag), succeeded: false);
            PipelineCachingLog.InvalidationEntryFailed(logger, CommandTypeName, CacheTelemetryNames.Target(isTag), ex);
            Activity.Current.AddExceptionEvents(ex);
        }
    }
}
