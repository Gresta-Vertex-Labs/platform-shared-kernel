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
}
