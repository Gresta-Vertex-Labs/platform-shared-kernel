using MediatR;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Short-circuits a duplicate command submission using a caller-supplied idempotency key.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IIdempotentRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Constrained to <see cref="ICommandBase"/> only — mirrors <c>TransactionBehavior</c>'s
/// commands-only constraint exactly; never applies to queries (a DI-level fact, since
/// <c>IQuery&lt;TResponse&gt;</c> never implements <see cref="ICommandBase"/>). Calls
/// <see cref="IIdempotencyKeyStore.HasProcessedAsync"/> first.
/// </para>
/// <para>
/// <b>Replay path (WO-039, P-242):</b> on <see langword="true"/> (duplicate), if the injected
/// <see cref="IIdempotencyKeyStore"/> also implements <see cref="IIdempotencyResponseStore"/>, this
/// behavior calls <see cref="IIdempotencyResponseStore.TryGetStoredResponseAsync"/> and, when a
/// stored value exists, deserializes and returns it directly — replaying the ORIGINAL outcome
/// (success or failure) exactly as it occurred, never a fresh <c>Error.Conflict</c>. When the store
/// does not support replay, or supports it but no stored value exists for this key (e.g. a
/// pre-replay-adoption key), this short-circuits WITHOUT calling <c>next()</c> a second time and
/// returns <c>Result.Failure(Error.Conflict(...))</c> — the original, still-default behavior.
/// </para>
/// <para>
/// On <see langword="false"/> (not a duplicate), calls <c>next()</c>, then calls
/// <see cref="IIdempotencyKeyStore.MarkProcessedAsync"/> so the key is recorded only after the
/// handler returns normally — a faulted handler must be safely retryable with the same key. When
/// replay is supported, the response is additionally serialized (via
/// <see cref="System.Text.Json.JsonSerializer"/>) and persisted via
/// <see cref="IIdempotencyResponseStore.StoreResponseAsync"/>, alongside <c>MarkProcessedAsync</c> —
/// neither call happens on a thrown exception, preserving the fail-and-consume-key invariant.
/// </para>
/// </remarks>
public sealed class IdempotentCommandBehavior<TRequest, TResponse>(IIdempotencyKeyStore idempotencyKeyStore)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IIdempotentRequest, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var hasProcessed = await idempotencyKeyStore
            .HasProcessedAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (hasProcessed)
        {
            if (idempotencyKeyStore is IIdempotencyResponseStore responseStore)
            {
                var stored = await responseStore
                    .TryGetStoredResponseAsync(request.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);

                if (stored is not null)
                    return IdempotencyResponseSerializer.Deserialize<TResponse>(stored);
            }

            var error = Error.Conflict(
                "idempotency.duplicate",
                $"A request with idempotency key '{request.IdempotencyKey}' has already been processed.");
            return FailureResponseFactory.Create<TResponse>(error);
        }

        var response = await next().ConfigureAwait(false);

        await idempotencyKeyStore
            .MarkProcessedAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (idempotencyKeyStore is IIdempotencyResponseStore replayStore)
        {
            var serialized = IdempotencyResponseSerializer.Serialize(response);
            await replayStore
                .StoreResponseAsync(request.IdempotencyKey, serialized, cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }
}
