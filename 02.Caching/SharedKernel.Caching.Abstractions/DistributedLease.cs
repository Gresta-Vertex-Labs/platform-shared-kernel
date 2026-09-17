namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A time-limited claim on a resource. It is never extended or released early; it simply expires.
/// </summary>
/// <remarks>
/// <para>
/// Use a lease when the claim itself is the goal, such as "this replica runs the 02:00 occurrence of a
/// job": releasing it when the work finishes would let a slower replica claim the same occurrence.
/// For a critical section that ends when the work ends, use a lock instead.
/// </para>
/// <para>
/// Choose a duration longer than any replica could lag behind, and include the occurrence in the
/// resource name (for example the scheduled time) so the next occurrence is a different resource.
/// </para>
/// </remarks>
/// <seealso cref="IDistributedLockService.TryAcquireLeaseAsync"/>
public sealed record DistributedLease
{
    /// <summary>Creates a lease.</summary>
    /// <param name="resource">The claimed resource name. Must not be null or whitespace.</param>
    /// <param name="fencingToken">The fencing token of this claim. Must be positive.</param>
    /// <param name="expiresAt">When the claim expires, as observed by the acquiring process.</param>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fencingToken"/> is not positive.</exception>
    public DistributedLease(string resource, long fencingToken, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fencingToken, 0);

        Resource = resource;
        FencingToken = fencingToken;
        ExpiresAt = expiresAt;
    }

    /// <summary>Gets the claimed resource name.</summary>
    public string Resource { get; }

    /// <summary>
    /// Gets a token that is strictly greater than the token of every earlier lock or lease on the same resource.
    /// </summary>
    public long FencingToken { get; }

    /// <summary>
    /// Gets when the claim expires, as observed by the acquiring process. Clock differences between
    /// machines make this approximate; do not rely on it to the millisecond.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }
}
