using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Evicts the cache key(s)/tag(s) declared by an <see cref="IInvalidatesCache"/> command after a
/// confirmed successful commit.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IInvalidatesCache"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Constrained to <see cref="ICommandBase"/> only, mirroring
/// <c>TransactionBehavior</c>/<c>IdempotentCommandBehavior</c>'s exact constraint shape — never
/// applies to queries (a DI-level fact). Calls <c>next()</c> first; only on success (the inner
/// pipeline returned without throwing — the same "did not fault" signal <c>TransactionBehavior</c>
/// uses, not an inspection of <c>Result.IsSuccess</c>/<c>IsFailure</c>) does it call
/// <see cref="ICacheService.RemoveAsync"/> once per declared
/// <see cref="IInvalidatesCache.CacheKeysToInvalidate"/> entry (and/or
/// <see cref="ICacheService.RemoveByTagAsync"/> per declared tag). Never calls removal on a thrown
/// exception — a faulted handler mutated nothing (or its mutation never committed, since this
/// behavior runs after <c>TransactionBehavior</c>), so there is nothing to evict. Positioned
/// innermost, immediately after <c>TransactionBehavior</c> — eviction must follow a confirmed
/// commit, never a speculative one.
/// </remarks>
public sealed class CacheInvalidationBehavior<TRequest, TResponse>(ICacheService cacheService)
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

        foreach (var key in request.CacheKeysToInvalidate)
        {
            await cacheService.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        foreach (var tag in request.CacheTagsToInvalidate)
        {
            await cacheService.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
