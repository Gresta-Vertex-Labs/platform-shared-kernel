using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Telemetry;

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
/// Uses the <see cref="IIdempotencyStore"/> registered for <see cref="IdempotencyPurpose.Request"/>. A nested command
/// (<see cref="ICommandScope.IsNested"/>) skips idempotency entirely and calls <c>next()</c> directly — the outer
/// command's own idempotency key already governs the whole unit of work.
/// </para>
/// <para>
/// An empty or whitespace-only <see cref="IIdempotentRequest.IdempotencyKey"/> fails with
/// <c>Error.Validation(</c><see cref="ErrorCodes.Idempotency.KeyRequired"/><c>, ...)</c>
/// (<c>idempotency.key_required</c>), the code <c>14.Presentation</c> answers a missing
/// <c>Idempotency-Key</c> header with.
/// </para>
/// <para>
/// <b>A reservation belongs to one caller of one tenant</b> (P-562 X3, merged from main in P-579). The key handed to
/// the store is never the command's raw key but a SHA-256 digest (64 lowercase hexadecimal characters) of the tenant,
/// the caller and the raw key, read from <see cref="IRequestContext"/>: actor kind, subject
/// (<see cref="IRequestContext.UserId"/>), client and impersonator (<see cref="IdempotencyKeyScope"/>). The session id
/// is left out, so a retry after a fresh sign-in still replays. Two callers who use the same key each get their own
/// reservation and their own execution, so a caller who learns another caller's key is never handed that caller's
/// stored response; the same caller retrying gets the replay. The store additionally partitions by the tenant of the
/// ambient request context.
/// </para>
/// <para>
/// <b>Anonymous callers share one scope per tenant.</b> A caller <see cref="IRequestContext"/> cannot identify
/// (<see cref="ActorKind.Anonymous"/>, no subject) differs from another only by its tenant, so all anonymous callers
/// of a tenant reserve in one scope — as do all callers of one actor kind whose context reports no identifiers. There
/// the request fingerprint is the only separation. An anonymous caller who knows another anonymous caller's key and
/// sends a request with the same fingerprint receives that caller's stored response; with a different fingerprint it
/// gets <c>idempotency.key_reused</c>. So keys must be unguessable (a random UUID per operation), an anonymous
/// command's response must carry nothing only its sender may see, and an explicit fingerprint on such a command must
/// cover every field that tells one sender's request from another's.
/// </para>
/// <para>
/// The request's fingerprint — its own <see cref="IIdempotentRequest.Fingerprint"/> when supplied (trimmed,
/// non-empty), otherwise a SHA-256 hash of its default JSON serialization — is passed to
/// <see cref="IIdempotencyStore.TryBeginAsync"/> together with the scoped key and
/// <see cref="IdempotencyBehaviorOptions.LeaseDuration"/>:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="IdempotencyReservationStatus.Started"/> — <c>next()</c> runs; on success the response is serialized and completed via
///   <see cref="IIdempotencyStore.CompleteAsync"/> for <see cref="IdempotencyBehaviorOptions.RetentionWindow"/>, passing back
///   <see cref="IdempotencyReservation.Token"/>; on a <c>Result.Failure</c> the reservation is released via
///   <see cref="IIdempotencyStore.ReleaseAsync"/> (never completed — a failed attempt must remain retryable); on a thrown
///   exception the reservation is likewise released and the exception is rethrown unchanged.</description></item>
///   <item><description><see cref="IdempotencyReservationStatus.InProgress"/> — <c>Error.Conflict(</c><see cref="ErrorCodes.Idempotency.InProgress"/><c>, ...)</c>, without calling <c>next()</c>.</description></item>
///   <item><description><see cref="IdempotencyReservationStatus.Completed"/> — the stored response is deserialized and returned directly, replaying the original outcome.</description></item>
///   <item><description><see cref="IdempotencyReservationStatus.FingerprintMismatch"/> — <c>Error.Conflict(</c><see cref="ErrorCodes.Idempotency.KeyReused"/><c>, ...)</c>.</description></item>
/// </list>
/// <para>
/// A <see langword="false"/> result from <see cref="IIdempotencyStore.CompleteAsync"/> means the reservation was
/// already lost (expired and reclaimed, or already completed/released) by the time the handler finished. The handler
/// already ran and its outcome is real, so the response is still returned — this behavior only logs a
/// <see cref="LogLevel.Warning"/>. A <see langword="false"/> result from <see cref="IIdempotencyStore.ReleaseAsync"/>
/// needs no log: the reservation being gone already is exactly what a release wants.
/// </para>
/// <para>
/// <b>The store call is the fault boundary.</b> An exception from <see cref="IIdempotencyStore.TryBeginAsync"/> fails
/// closed: a failed <c>Result</c> of <c>Error.Unavailable(</c><see cref="ErrorCodes.Idempotency.StoreUnavailable"/><c>, ...)</c>
/// and the handler does not run; a <see cref="SharedKernelException"/> from the store passes its own <c>Error</c>
/// through; the caller's own cancellation propagates. A fault in <see cref="IIdempotencyStore.CompleteAsync"/> or
/// <see cref="IIdempotencyStore.ReleaseAsync"/> is logged and swallowed and never replaces the handler's outcome or
/// exception — the reservation expires on its lease. This behavior never catches around <c>next()</c> except to release
/// the reservation before rethrowing.
/// </para>
/// <para>The idempotency key itself is never echoed in any error message returned to the caller.</para>
/// </remarks>
internal sealed class IdempotencyBehavior<TRequest, TResponse>(
    [FromKeyedServices(IdempotencyPurpose.Request)] IIdempotencyStore store,
    IRequestContext requestContext,
    ICommandScope commandScope,
    IOptions<IdempotencyBehaviorOptions> options,
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
            return FailureResponse.Create<TResponse>(ApplicationErrors.IdempotencyKeyRequired());
        }

        var key = IdempotencyKeyScope.Create(requestContext, request.IdempotencyKey);
        var fingerprint = string.IsNullOrWhiteSpace(request.Fingerprint)
            ? RequestFingerprint.Compute(request)
            : request.Fingerprint.Trim();
        var settings = options.Value;

        IdempotencyReservation reservation;
        try
        {
            reservation = await store
                .TryBeginAsync(IdempotencyPurpose.Request, key, fingerprint, settings.LeaseDuration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation is not a store fault.
            throw;
        }
        catch (Exception exception)
        {
            // The store call is the fault boundary: this package cannot reference a provider to classify its
            // exceptions, and IIdempotencyStore documents that an unreachable store throws. Fail closed: the handler
            // does not run. A classified provider exception keeps its own Error.
            var classified = exception as SharedKernelException;
            PipelineLog.IdempotencyStoreUnavailable(
                logger,
                classified is null ? LogLevel.Error : LogLevel.Warning,
                typeof(TRequest).FullName ?? typeof(TRequest).Name,
                exception);
            Activity.Current.AddExceptionEvents(exception);

            return FailureResponse.Create<TResponse>(classified?.Error ?? ApplicationErrors.IdempotencyStoreUnavailable());
        }

        switch (reservation.Status)
        {
            case IdempotencyReservationStatus.Completed:
                return IdempotencyResponseSerializer.Deserialize<TResponse>(
                    reservation.StoredResponse
                    ?? throw new InvalidOperationException(
                        $"The idempotency store '{store.GetType().Name}' reported a completed request reservation without a stored response."));

            case IdempotencyReservationStatus.InProgress:
                return FailureResponse.Create<TResponse>(ApplicationErrors.IdempotencyInProgress());

            case IdempotencyReservationStatus.FingerprintMismatch:
                return FailureResponse.Create<TResponse>(ApplicationErrors.IdempotencyKeyReused());

            case IdempotencyReservationStatus.Started:
                return await RunAndRecordAsync(
                        next,
                        key,
                        reservation.Token
                        ?? throw new InvalidOperationException(
                            $"The idempotency store '{store.GetType().Name}' returned a started reservation without a token."),
                        settings.RetentionWindow,
                        cancellationToken)
                    .ConfigureAwait(false);

            default:
                // Fail closed: an undefined status must never run the handler, because the store's answer is unknown.
                throw new UnreachableException(
                    $"The idempotency store '{store.GetType().Name}' returned the undefined reservation status '{reservation.Status}'.");
        }
    }

    private async Task<TResponse> RunAndRecordAsync(
        RequestHandlerContinuation<TResponse> next,
        string key,
        string token,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        TResponse response;
        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch
        {
            // The handler's exception must propagate, so a fault while releasing never replaces it.
            await ReleaseQuietlyAsync(key, token, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (ResponseOutcome.IsSuccess(response))
        {
            var serialized = IdempotencyResponseSerializer.Serialize(response);
            try
            {
                var completed = await store
                    .CompleteAsync(IdempotencyPurpose.Request, key, token, serialized, retention, cancellationToken)
                    .ConfigureAwait(false);

                if (!completed)
                    PipelineLog.IdempotencyReservationLost(logger, typeof(TRequest).FullName ?? typeof(TRequest).Name);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Documented catch-all (rule 27): the work already committed, so the handler's response is returned and
                // the reservation expires on its lease. The caller's own cancellation is not caught.
                PipelineLog.IdempotencyCompleteFailed(logger, typeof(TRequest).FullName ?? typeof(TRequest).Name, exception);
                Activity.Current.AddExceptionEvents(exception);
            }
        }
        else
        {
            await ReleaseQuietlyAsync(key, token, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private async Task ReleaseQuietlyAsync(string key, string token, CancellationToken cancellationToken)
    {
        try
        {
            await store.ReleaseAsync(IdempotencyPurpose.Request, key, token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Documented catch-all (rule 27): never replaces the handler's outcome or exception; the reservation
            // expires on its lease. The caller's own cancellation is not caught.
            PipelineLog.IdempotencyReleaseFailed(logger, typeof(TRequest).FullName ?? typeof(TRequest).Name, exception);
            Activity.Current.AddExceptionEvents(exception);
        }
    }
}
