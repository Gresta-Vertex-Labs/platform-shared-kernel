using SharedKernel.Application.Commands;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline.Idempotency;

/// <summary>
/// Short-circuits a duplicate command submission using a caller-supplied idempotency key.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IIdempotentRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// A nested command (<see cref="ICommandScope.IsNested"/>) skips idempotency entirely and calls
/// <c>next()</c> directly — the outer command's own idempotency key already governs the whole unit
/// of work.
/// </para>
/// <para>
/// An empty or whitespace-only <see cref="IIdempotentRequest.IdempotencyKey"/> fails with
/// <c>Error.Validation("idempotency.key_missing", ...)</c>. Otherwise, the request's fingerprint —
/// its own <see cref="IIdempotentRequest.Fingerprint"/> when supplied (trimmed, non-empty),
/// otherwise a SHA-256 hash of its default JSON serialization — is computed and passed to
/// <see cref="IRequestIdempotencyStore.TryBeginAsync"/>:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IdempotencyBeginStatus.Started"/> — <c>next()</c> runs; on success the response is serialized and completed via
///   <see cref="IRequestIdempotencyStore.CompleteAsync"/>, passing back the reservation token from
///   <see cref="IdempotencyBeginResult.ReservationToken"/>; on a <c>Result.Failure</c> the reservation is
///   released via <see cref="IRequestIdempotencyStore.ReleaseAsync"/> (never completed — a failed
///   attempt must remain retryable); on a thrown exception the reservation is likewise released and
///   the exception is rethrown unchanged.</description></item>
///   <item><description><see cref="IdempotencyBeginStatus.InProgress"/> — <c>Error.Conflict("idempotency.in_progress", ...)</c>, without calling <c>next()</c>.</description></item>
///   <item><description><see cref="IdempotencyBeginStatus.Completed"/> — the stored response is deserialized and returned directly, replaying the original outcome.</description></item>
///   <item><description><see cref="IdempotencyBeginStatus.FingerprintMismatch"/> — <c>Error.Conflict("idempotency.key_reused", ...)</c>.</description></item>
/// </list>
/// <para>
/// A <see langword="false"/> result from <see cref="IRequestIdempotencyStore.CompleteAsync"/> means
/// the reservation was already lost (expired and reclaimed, or already completed/released) by the
/// time the handler finished. The handler already ran and its outcome is real, so the response is
/// still returned to the caller — this behavior only logs a <see cref="LogLevel.Warning"/>. A
/// <see langword="false"/> result from <see cref="IRequestIdempotencyStore.ReleaseAsync"/> needs no
/// log — the reservation being gone already is exactly the outcome a release call wants.
/// </para>
/// <para>The idempotency key itself is never echoed in any error message returned to the caller.</para>
/// </remarks>
public sealed partial class IdempotencyBehavior<TRequest, TResponse>(
    IRequestIdempotencyStore store,
    ICommandScope commandScope,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IIdempotentRequest, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (commandScope.IsNested)
            return await next().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return FailureResponse.Create<TResponse>(
                Error.Validation("idempotency.key_missing", "An idempotency key is required."));
        }

        var key = request.IdempotencyKey;
        var fingerprint = string.IsNullOrWhiteSpace(request.Fingerprint)
            ? RequestFingerprint.Compute(request)
            : request.Fingerprint.Trim();

        var begin = await store.TryBeginAsync(key, fingerprint, cancellationToken).ConfigureAwait(false);

        switch (begin.Status)
        {
            case IdempotencyBeginStatus.Completed:
                return IdempotencyResponseSerializer.Deserialize<TResponse>(begin.StoredResponse!);

            case IdempotencyBeginStatus.InProgress:
                return FailureResponse.Create<TResponse>(
                    Error.Conflict(
                        "idempotency.in_progress",
                        "A request with this idempotency key is still being processed."));

            case IdempotencyBeginStatus.FingerprintMismatch:
                return FailureResponse.Create<TResponse>(
                    Error.Conflict(
                        "idempotency.key_reused",
                        "The idempotency key was already used for a different request."));

            case IdempotencyBeginStatus.Started:
            default:
                return await RunAndRecordAsync(next, store, key, begin.ReservationToken!, logger, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private static async Task<TResponse> RunAndRecordAsync(
        RequestHandlerContinuation<TResponse> next,
        IRequestIdempotencyStore store,
        string key,
        string reservationToken,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        TResponse response;
        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch
        {
            await store.ReleaseAsync(key, reservationToken, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (ResponseOutcome.IsSuccess(response))
        {
            var serialized = IdempotencyResponseSerializer.Serialize(response);
            var completed = await store.CompleteAsync(key, reservationToken, serialized, cancellationToken)
                .ConfigureAwait(false);

            if (!completed)
                LogCompleteReservationLost(logger, typeof(TRequest).FullName ?? typeof(TRequest).Name);
        }
        else
        {
            await store.ReleaseAsync(key, reservationToken, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// The reservation was lost before <c>CompleteAsync</c> could confirm it — the handler already
    /// ran successfully, so the response is returned to the caller regardless (Warning).
    /// </summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogCompleteReservationLost,
        Level = LogLevel.Warning,
        Message = "Completed handling {RequestType} but its idempotency reservation was lost before CompleteAsync could confirm it; returning the response anyway")]
    private static partial void LogCompleteReservationLost(ILogger logger, string requestType);
}
