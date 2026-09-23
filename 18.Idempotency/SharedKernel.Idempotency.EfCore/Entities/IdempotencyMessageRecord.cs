namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>
/// Backing row for <c>SharedKernel.Messaging.Abstractions.Idempotency.IIdempotencyStore</c>
/// (D-06). Plain persistence-shape record, not a domain aggregate — internal, never exposed on
/// this package's public API surface.
/// </summary>
internal sealed class IdempotencyMessageRecord
{
    /// <summary>The tenant this row belongs to. Never simply omitted for a null tenant context — see <c>EfCoreTenantScope</c>.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The message identifier, sourced from <c>ConsumeContext.MessageId</c>.</summary>
    public required Guid MessageId { get; init; }

    /// <summary>When this reservation was created or last reclaimed.</summary>
    public required DateTimeOffset ReservedAtUtc { get; set; }

    /// <summary>
    /// When this row stops blocking a fresh reservation for the same <see cref="TenantId"/>/<see cref="MessageId"/>.
    /// A short in-flight value until <c>MarkProcessedAsync</c> extends it to the full retention window.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// Opaque token identifying the delivery that currently holds the reservation.
    /// </summary>
    /// <remarks>
    /// Checked by Complete and Release so a delivery whose lease already expired — and whose id was
    /// taken over by a redelivery — cannot overwrite the new holder's record (P-560).
    /// </remarks>
    public required string ReservationToken { get; set; }

    /// <summary>
    /// When the message finished consuming, or <see langword="null"/> while it is still in flight.
    /// </summary>
    /// <remarks>
    /// This is what separates "already processed" from "another delivery is running", which the
    /// previous single-boolean contract could not express.
    /// </remarks>
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
