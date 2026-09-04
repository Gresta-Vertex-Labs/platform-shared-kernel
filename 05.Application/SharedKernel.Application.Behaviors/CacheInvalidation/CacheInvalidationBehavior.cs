using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Evicts the cache key(s)/tag(s) declared by an <see cref="IInvalidatesCache"/> command after a
/// non-faulted handler run.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IInvalidatesCache"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Constrained to <see cref="ICommandBase"/> only, mirroring
/// <c>TransactionBehavior</c>/<c>IdempotentCommandBehavior</c>'s exact constraint shape — never
/// applies to queries (a DI-level fact). Calls <c>next()</c> first; only on a <b>non-faulted</b>
/// outcome (the inner pipeline returned without throwing) does it call
/// <see cref="ICacheService.RemoveAsync"/> once per declared
/// <see cref="IInvalidatesCache.CacheKeysToInvalidate"/> entry (and/or
/// <see cref="ICacheService.RemoveByTagAsync"/> per declared tag). Never calls removal on a thrown
/// exception — a faulted handler mutated nothing (or its mutation never committed, since this
/// behavior's eviction is ordered to observably follow <c>TransactionBehavior</c>'s commit), so
/// there is nothing to evict.
/// </para>
/// <para>
/// <b>Ordered relative to <c>TransactionBehavior</c> (WO-080, P-488):</b> registered OUTER to
/// (i.e. BEFORE, in <c>ApplicationBehaviorsBuilder.Build()</c>) <c>TransactionBehavior</c>, so this
/// behavior's post-<c>next()</c> eviction call is observed strictly AFTER
/// <c>TransactionBehavior</c>'s own post-<c>next()</c> <c>IUnitOfWork.SaveChangesAsync</c>
/// commit has completed — never before it. Do not read "outer" here as "further from the handler
/// in call order" — MediatR's first-registered-is-outermost rule means an outer behavior's
/// post-<c>next()</c> code runs LAST, after every inner behavior (including
/// <c>TransactionBehavior</c> and, inside it, <c>AuditingBehavior</c>) has already fully unwound.
/// Eviction must follow a confirmed commit, never a speculative one — proven by a real composed
/// pipeline dispatch, not merely asserted here (<c>CacheInvalidationTransactionOrderingTests</c>).
/// </para>
/// <para>
/// <b>Default stance — always invalidate on non-throw (documented, not an oversight):</b>
/// a <c>Result.Failure</c> from the handler <b>still</b> triggers cache eviction in the default
/// mode (<see cref="InvalidateOnlyOnSuccess"/> = <see langword="false"/>). This mirrors
/// <c>TransactionBehavior</c>'s "do not inspect <c>Result.IsSuccess</c>/<c>IsFailure</c>"
/// principle — a <c>Result.Failure</c> is a valid, deliberate handler outcome, not a fault;
/// whether to stage a mutation before returning failure is the handler's own decision. Operations
/// teams reading the code must not assume a <c>Result.Failure</c> prevents eviction — it does
/// <b>not</b> by default.
/// </para>
/// <para>
/// <b>Opt-in strict mode — <see cref="InvalidateOnlyOnSuccess"/> = <see langword="true"/>:</b>
/// when enabled, the behavior additionally gates eviction on
/// <see cref="IHasSuccessFlag.IsSuccess"/> (available from <c>SharedKernel.Primitives</c>
/// P-230). If <typeparamref name="TResponse"/> implements <see cref="IHasSuccessFlag"/> and
/// <c>IsSuccess</c> is <see langword="false"/>, no eviction occurs. If
/// <typeparamref name="TResponse"/> does not implement <see cref="IHasSuccessFlag"/>, the
/// behavior falls back to the default always-invalidate-on-non-throw stance.
/// This flag does <b>not</b> change the thrown-exception path — removal is never called on a
/// thrown exception regardless of this flag, since a faulted handler mutated nothing.
/// </para>
/// </remarks>
public sealed class CacheInvalidationBehavior<TRequest, TResponse>(ICacheService cacheService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IInvalidatesCache, IRequest<TResponse>
{
    /// <summary>
    /// Gets or sets a value indicating whether cache eviction is gated on the response's
    /// <see cref="IHasSuccessFlag.IsSuccess"/> flag.
    /// </summary>
    /// <value>
    /// <see langword="false"/> (default) — evicts cache entries whenever <c>next()</c> returns
    /// without throwing, regardless of whether the response represents success or failure.
    /// A <c>Result.Failure</c> from the handler still triggers eviction in this mode (see class
    /// remarks for rationale).
    /// <br/>
    /// <see langword="true"/> — evicts only when the response implements
    /// <see cref="IHasSuccessFlag"/> and <see cref="IHasSuccessFlag.IsSuccess"/> is
    /// <see langword="true"/>. Responses that do not implement <see cref="IHasSuccessFlag"/>
    /// fall back to the always-invalidate stance.
    /// </value>
    public bool InvalidateOnlyOnSuccess { get; init; }

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        // Gate eviction: when InvalidateOnlyOnSuccess is true, skip eviction for a failure result.
        if (InvalidateOnlyOnSuccess
            && response is IHasSuccessFlag flag
            && !flag.IsSuccess)
        {
            return response;
        }

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
