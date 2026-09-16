using MediatR;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Evicts the cache key(s)/tag(s) declared by an <see cref="IInvalidatesCache"/> command after the
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
/// <c>SharedKernel.Application.Behaviors.Transaction.TransactionBehavior</c>'s commit has already
/// completed, regardless of where this behavior itself sits in the registration order. A
/// <c>Result.Failure</c> or a thrown exception registers nothing — there is nothing to evict.
/// </para>
/// <para>
/// Tenant scoping mirrors <see cref="Caching.CachingBehavior{TRequest,TResponse}"/> exactly: when
/// <see cref="IRequestContext"/> is registered and its <see cref="IRequestContext.TenantId"/> is
/// non-null, every key and tag is rewritten as <c>tenant:{tenantId}:{value}</c> before eviction, so
/// this can never evict another tenant's entries.
/// </para>
/// </remarks>
public sealed class CacheInvalidationBehavior<TRequest, TResponse>(
    ICacheService cacheService,
    ICommandScope commandScope,
    IRequestContext? requestContext = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IInvalidatesCache, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        if (response is not IHasSuccessFlag { IsSuccess: false })
        {
            var tenantId = requestContext?.TenantId;
            var keys = request.CacheKeysToInvalidate;
            var tags = request.CacheTagsToInvalidate;

            commandScope.OnCompleted(async ct =>
            {
                foreach (var key in keys)
                {
                    var scopedKey = tenantId is null ? key : $"tenant:{tenantId}:{key}";
                    await cacheService.RemoveAsync(scopedKey, ct).ConfigureAwait(false);
                }

                foreach (var tag in tags)
                {
                    var scopedTag = tenantId is null ? tag : $"tenant:{tenantId}:{tag}";
                    await cacheService.RemoveByTagAsync(scopedTag, ct).ConfigureAwait(false);
                }
            });
        }

        return response;
    }
}
