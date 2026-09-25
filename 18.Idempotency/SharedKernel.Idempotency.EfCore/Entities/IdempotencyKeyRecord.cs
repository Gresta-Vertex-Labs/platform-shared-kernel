namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>
/// Backing row for <c>SharedKernel.Application.Pipeline.Idempotency.IRequestIdempotencyStore</c>.
/// Plain persistence-shape record, not a domain aggregate — internal, never exposed on this
/// package's public API surface.
/// </summary>
internal sealed class IdempotencyKeyRecord
{
    /// <summary>The tenant this row belongs to. Never simply omitted for a null tenant context — see <c>EfCoreTenantScope</c>.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The caller-supplied idempotency key.</summary>
    public required string Key { get; init; }

    /// <summary>
    /// A fingerprint of the request payload that reserved this key, used to detect the key being
    /// reused for a genuinely different request.
    /// </summary>
    public required string Fingerprint { get; set; }

    /// <summary>Whether this reservation has been completed yet.</summary>
    public required IdempotencyRecordStatus Status { get; set; }

    /// <summary>
    /// An opaque, per-reservation identifier generated fresh by every winning
    /// <c>TryBeginAsync</c> call, and required to match on every subsequent <c>CompleteAsync</c>/
    /// <c>ReleaseAsync</c> call against this row — guards against a stale confirm/release
    /// corrupting a different caller's reservation after this one was reclaimed as expired.
    /// </summary>
    public required Guid ReservationToken { get; set; }

    /// <summary>When this reservation was created or last reclaimed.</summary>
    public required DateTimeOffset ReservedAtUtc { get; set; }

    /// <summary>
    /// When this row stops blocking a fresh reservation for the same <see cref="TenantId"/>/<see cref="Key"/>.
    /// A short in-flight value until <c>CompleteAsync</c> extends it to the full retention window.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// The opaque, caller-supplied serialized response, or <see langword="null"/> when no response
    /// has been stored for this key yet. Persisted exactly as given — never inspected, reshaped, or
    /// re-serialized.
    /// </summary>
    public string? Response { get; set; }
}
