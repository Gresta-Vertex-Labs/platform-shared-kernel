namespace SharedKernel.Idempotency.Abstractions;

/// <summary>The result of <see cref="IIdempotencyStore.TryBeginAsync"/>.</summary>
/// <param name="Status">Whether the caller now holds the reservation, and if not, why.</param>
/// <param name="StoredResponse">
/// The response recorded by <see cref="IIdempotencyStore.CompleteAsync"/>. Set only when
/// <paramref name="Status"/> is <see cref="IdempotencyReservationStatus.Completed"/> and a response was stored —
/// a completed message reservation has none.
/// </param>
/// <param name="Token">
/// The opaque token proving ownership of the reservation. Set if and only if <paramref name="Status"/> is
/// <see cref="IdempotencyReservationStatus.Started"/>. Pass it back to <see cref="IIdempotencyStore.CompleteAsync"/>
/// or <see cref="IIdempotencyStore.ReleaseAsync"/>: that is what lets a store stay stateless, and what turns a late
/// call from a caller whose reservation expired and was taken over into a harmless no-op.
/// </param>
public readonly record struct IdempotencyReservation(
    IdempotencyReservationStatus Status,
    string? StoredResponse,
    string? Token)
{
    /// <summary>The caller now holds the reservation.</summary>
    /// <param name="token">The ownership token to pass back on completion or release.</param>
    /// <returns>A <see cref="IdempotencyReservationStatus.Started"/> reservation.</returns>
    public static IdempotencyReservation Started(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new(IdempotencyReservationStatus.Started, null, token);
    }

    /// <summary>Another caller holds the reservation and has not finished.</summary>
    /// <returns>An <see cref="IdempotencyReservationStatus.InProgress"/> reservation.</returns>
    public static IdempotencyReservation InProgress() => new(IdempotencyReservationStatus.InProgress, null, null);

    /// <summary>The key was already completed with the same fingerprint.</summary>
    /// <param name="storedResponse">The stored response, or <see langword="null"/> when none was recorded.</param>
    /// <returns>A <see cref="IdempotencyReservationStatus.Completed"/> reservation.</returns>
    public static IdempotencyReservation Completed(string? storedResponse) =>
        new(IdempotencyReservationStatus.Completed, storedResponse, null);

    /// <summary>The key exists for a different fingerprint.</summary>
    /// <returns>A <see cref="IdempotencyReservationStatus.FingerprintMismatch"/> reservation.</returns>
    public static IdempotencyReservation FingerprintMismatch() =>
        new(IdempotencyReservationStatus.FingerprintMismatch, null, null);
}
