namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// A minimal seam for atomically reserving, completing, and releasing an in-process command
/// idempotency key.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Messaging.Abstractions.IIdempotencyStore</c>
/// (<c>07.Messaging</c>'s consumer-side deduplication contract) — <c>05.Application</c> can never
/// reference <c>07.Messaging</c> (layering runs the other direction), so
/// <see cref="IdempotencyBehavior{TRequest,TResponse}"/> depends on this local interface instead.
/// The consuming service (or <c>18.Idempotency</c>) provides the implementation and registers it at
/// the composition root. This package ships only the interface — no implementation.
/// </para>
/// <para>
/// <b>Key scoping.</b> The key itself is opaque to this contract; scoping it by tenant, if needed,
/// is the store implementation's responsibility.
/// </para>
/// <para>
/// <b>Contract.</b> <see cref="TryBeginAsync"/> atomically reserves a new key and records the
/// request's fingerprint. Calling it again for a key that is reserved but not yet completed returns
/// <see cref="IdempotencyBeginStatus.InProgress"/>. Calling it again for a key that was already
/// completed with the <b>same</b> fingerprint returns <see cref="IdempotencyBeginStatus.Completed"/>
/// together with the stored response. Calling it for a key that exists (in-flight or completed)
/// against a <b>different</b> fingerprint returns <see cref="IdempotencyBeginStatus.FingerprintMismatch"/>.
/// A reservation that is never completed or released expires after a store-defined in-flight TTL,
/// so a crashed process can never permanently wedge a key.
/// </para>
/// <para>
/// <b>Statelessness.</b> A winning <see cref="TryBeginAsync"/> call (<see cref="IdempotencyBeginStatus.Started"/>)
/// returns an opaque <see cref="IdempotencyBeginResult.ReservationToken"/> that the caller must pass
/// back to <see cref="CompleteAsync"/>/<see cref="ReleaseAsync"/>. The store implementation never
/// remembers which caller won a reservation itself — the token round trip is what lets an
/// implementation be a thin, stateless wrapper over its backing store (no per-instance dictionary of
/// "reservations I have won"), and what makes a late confirm/release from a caller whose reservation
/// already expired and was reclaimed by someone else a safe, detectable no-op instead of silent
/// corruption of the new owner's row.
/// </para>
/// </remarks>
public interface IRequestIdempotencyStore
{
    /// <summary>
    /// Attempts to atomically reserve <paramref name="key"/> for a new execution, or reports its
    /// existing state.
    /// </summary>
    /// <param name="key">The idempotency key supplied by the command instance.</param>
    /// <param name="requestFingerprint">
    /// A fingerprint of the request payload, used to detect the key being reused for a genuinely
    /// different request.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Marks <paramref name="key"/> as completed and persists its serialized response for future
    /// replay.
    /// </summary>
    /// <param name="key">The idempotency key, previously reserved via <see cref="TryBeginAsync"/>.</param>
    /// <param name="reservationToken">
    /// The <see cref="IdempotencyBeginResult.ReservationToken"/> returned by the winning
    /// <see cref="TryBeginAsync"/> call.
    /// </param>
    /// <param name="serializedResponse">The response, already serialized by the caller.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="reservationToken"/> still owned the reservation and
    /// the completion was applied; <see langword="false"/> if the reservation was lost — it expired
    /// and was reclaimed by another caller, it was already completed or released, or
    /// <paramref name="reservationToken"/> simply does not match. A <see langword="false"/> result
    /// never throws; the caller already ran the handler successfully and must still return its
    /// response.
    /// </returns>
    Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a reservation on <paramref name="key"/> without completing it, so a future request
    /// with the same key may be reserved again.
    /// </summary>
    /// <param name="key">The idempotency key, previously reserved via <see cref="TryBeginAsync"/>.</param>
    /// <param name="reservationToken">
    /// The <see cref="IdempotencyBeginResult.ReservationToken"/> returned by the winning
    /// <see cref="TryBeginAsync"/> call.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="reservationToken"/> still owned the reservation and
    /// it was released; <see langword="false"/> if the reservation was already lost (expired and
    /// reclaimed, already completed or released, or a foreign token).
    /// </returns>
    /// <remarks>
    /// Called when the command faults (a thrown exception) or fails (a <c>Result.Failure</c>) — see
    /// <see cref="IdempotencyBehavior{TRequest,TResponse}"/>'s remarks for the exact rule.
    /// </remarks>
    Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken);
}
