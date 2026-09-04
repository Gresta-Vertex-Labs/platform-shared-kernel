namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>
/// Backing row for <c>SharedKernel.Application.Behaviors.Idempotency.IIdempotencyKeyStore</c> /
/// <c>IIdempotencyResponseStore</c> (D-06). Plain persistence-shape record, not a domain
/// aggregate — internal, never exposed on this package's public API surface.
/// </summary>
internal sealed class IdempotencyKeyRecord
{
    /// <summary>The tenant this row belongs to. Never simply omitted for a null tenant context — see <c>EfCoreTenantScope</c>.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The caller-supplied idempotency key.</summary>
    public required string Key { get; init; }

    /// <summary>When this reservation was created or last reclaimed.</summary>
    public required DateTimeOffset ReservedAtUtc { get; set; }

    /// <summary>
    /// When this row stops blocking a fresh reservation for the same <see cref="TenantId"/>/<see cref="Key"/>.
    /// A short in-flight value until <c>MarkProcessedAsync</c> extends it to the full retention window.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// The opaque, caller-supplied serialized response, or <see langword="null"/> when no response
    /// has been stored for this key yet. Persisted exactly as given — never inspected, reshaped, or
    /// re-serialized (Domain Invariant 6).
    /// </summary>
    public string? Response { get; set; }
}
