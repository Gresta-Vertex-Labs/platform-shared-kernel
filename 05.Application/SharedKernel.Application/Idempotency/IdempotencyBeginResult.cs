namespace SharedKernel.Application.Idempotency;

/// <summary>
/// The result of an <see cref="IRequestIdempotencyStore.TryBeginAsync"/> call.
/// </summary>
/// <param name="Status">The outcome of the begin attempt.</param>
/// <param name="StoredResponse">
/// The previously-stored, serialized response — set only when <paramref name="Status"/> is
/// <see cref="IdempotencyBeginStatus.Completed"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="ReservationToken">
/// An opaque token identifying the reservation this call created — set if and only if
/// <paramref name="Status"/> is <see cref="IdempotencyBeginStatus.Started"/>; otherwise
/// <see langword="null"/>. The caller must pass it back to
/// <see cref="IRequestIdempotencyStore.CompleteAsync"/>/<see cref="IRequestIdempotencyStore.ReleaseAsync"/>
/// so the store can be implemented statelessly — it never remembers which caller won a reservation.
/// </param>
public readonly record struct IdempotencyBeginResult(
    IdempotencyBeginStatus Status,
    string? StoredResponse,
    string? ReservationToken)
{
    /// <summary>Creates a result indicating a new reservation was created.</summary>
    /// <param name="reservationToken">
    /// The opaque token identifying this reservation, to be passed back to
    /// <see cref="IRequestIdempotencyStore.CompleteAsync"/>/<see cref="IRequestIdempotencyStore.ReleaseAsync"/>.
    /// </param>
    public static IdempotencyBeginResult Started(string reservationToken)
        => new(IdempotencyBeginStatus.Started, null, reservationToken);

    /// <summary>Creates a result indicating the key is reserved by an in-flight execution.</summary>
    public static IdempotencyBeginResult InProgress() => new(IdempotencyBeginStatus.InProgress, null, null);

    /// <summary>Creates a result indicating the key was already completed, carrying its stored response.</summary>
    /// <param name="storedResponse">The previously-stored, serialized response.</param>
    public static IdempotencyBeginResult Completed(string storedResponse)
        => new(IdempotencyBeginStatus.Completed, storedResponse, null);

    /// <summary>Creates a result indicating the key exists against a different request fingerprint.</summary>
    public static IdempotencyBeginResult FingerprintMismatch() => new(IdempotencyBeginStatus.FingerprintMismatch, null, null);
}
