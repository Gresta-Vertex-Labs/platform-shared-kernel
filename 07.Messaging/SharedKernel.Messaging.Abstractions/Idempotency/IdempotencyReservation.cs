namespace SharedKernel.Messaging.Abstractions.Idempotency;

/// <summary>
/// The result of attempting to reserve a message id for consumption.
/// </summary>
/// <remarks>
/// Shaped after <c>05.Application</c>'s <c>IdempotencyBeginResult</c> so the platform has one
/// reservation vocabulary rather than two. It carries no stored response: a message consumer
/// returns no value, so a completed duplicate is simply acknowledged.
/// </remarks>
/// <param name="Status">Whether this delivery owns the reservation.</param>
/// <param name="ReservationToken">
/// Opaque token proving ownership, non-<see langword="null"/> only when
/// <paramref name="Status"/> is <see cref="IdempotencyReservationStatus.Started"/>. It must be
/// passed back to <see cref="IIdempotencyStore.CompleteAsync"/> or
/// <see cref="IIdempotencyStore.ReleaseAsync"/> so a store can reject a call from a delivery that
/// no longer holds the reservation — for example one whose lease already expired and was taken
/// over by a redelivery.
/// </param>
public readonly record struct IdempotencyReservation(
    IdempotencyReservationStatus Status,
    string? ReservationToken)
{
    /// <summary>This delivery acquired the reservation.</summary>
    /// <param name="reservationToken">The ownership token to pass back on completion or release.</param>
    /// <returns>A <see cref="IdempotencyReservationStatus.Started"/> reservation.</returns>
    public static IdempotencyReservation Started(string reservationToken) =>
        new(IdempotencyReservationStatus.Started, reservationToken);

    /// <summary>Another delivery holds the reservation and is still running.</summary>
    /// <returns>An <see cref="IdempotencyReservationStatus.InProgress"/> reservation.</returns>
    public static IdempotencyReservation InProgress() =>
        new(IdempotencyReservationStatus.InProgress, null);

    /// <summary>The message was already consumed to completion.</summary>
    /// <returns>An <see cref="IdempotencyReservationStatus.AlreadyProcessed"/> reservation.</returns>
    public static IdempotencyReservation AlreadyProcessed() =>
        new(IdempotencyReservationStatus.AlreadyProcessed, null);
}
