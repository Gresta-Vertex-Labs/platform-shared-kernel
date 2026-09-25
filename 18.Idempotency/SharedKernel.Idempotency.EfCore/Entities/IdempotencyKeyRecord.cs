using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>
/// Backing row for <see cref="IIdempotencyStore"/>, one per (tenant scope, purpose, key). Plain persistence shape —
/// internal, never exposed on this package's public API.
/// </summary>
internal sealed class IdempotencyKeyRecord
{
    /// <summary>The tenant scope (<see cref="IdempotencyTenantScope"/>): a tenant id in "D" form, or <c>no-tenant</c>.</summary>
    public required string TenantScope { get; init; }

    /// <summary>What the key guards; request keys and message ids never share a row.</summary>
    public required IdempotencyPurpose Purpose { get; init; }

    /// <summary>The idempotency key (a request's key, or a message id in "D" form).</summary>
    public required string Key { get; init; }

    /// <summary>What the key was reserved for; the same key with a different fingerprint is a mismatch.</summary>
    public required string Fingerprint { get; set; }

    /// <summary>Whether the reservation has been completed.</summary>
    public required IdempotencyRecordStatus Status { get; set; }

    /// <summary>
    /// Generated fresh by every winning <c>TryBeginAsync</c> and required to match on <c>CompleteAsync</c>/
    /// <c>ReleaseAsync</c>, so a stale caller cannot touch a reservation taken over after its own expired.
    /// </summary>
    public required Guid ReservationToken { get; set; }

    /// <summary>When the reservation was created or last reclaimed.</summary>
    public required DateTimeOffset ReservedAtUtc { get; set; }

    /// <summary>
    /// When the row stops blocking a fresh reservation: the caller's in-flight TTL until completion, then the
    /// caller's retention window.
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>The stored response, persisted exactly as given; <see langword="null"/> when none was stored.</summary>
    public string? Response { get; set; }
}
