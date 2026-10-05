namespace SharedKernel.Idempotency.Abstractions;

/// <summary>The outcome of <see cref="IIdempotencyStore.TryBeginAsync"/>.</summary>
/// <remarks>
/// <see cref="InProgress"/> and <see cref="Completed"/> must stay distinct. A single "seen" flag cannot tell a
/// delivery that is still running from one that finished, and treating the first as the second acknowledges a
/// redelivered message whose original attempt then fails — the message is lost.
/// </remarks>
public enum IdempotencyReservationStatus
{
    /// <summary>
    /// The key was free (or its previous reservation expired). The caller now holds the reservation, must run the
    /// guarded work, then call <see cref="IIdempotencyStore.CompleteAsync"/> on success or
    /// <see cref="IIdempotencyStore.ReleaseAsync"/> on failure.
    /// </summary>
    Started,

    /// <summary>
    /// Another caller holds the reservation and has not finished. A request is rejected as a conflict; a message is
    /// left unacknowledged so the broker redelivers it and it is still processed if the running attempt fails.
    /// </summary>
    InProgress,

    /// <summary>
    /// The key was completed with the same fingerprint. A request replays
    /// <see cref="IdempotencyReservation.StoredResponse"/>; a message is acknowledged without running the consumer.
    /// </summary>
    Completed,

    /// <summary>
    /// The key exists — in flight or completed — for a different fingerprint: the same key was reused for a
    /// different request.
    /// </summary>
    FingerprintMismatch,
}
