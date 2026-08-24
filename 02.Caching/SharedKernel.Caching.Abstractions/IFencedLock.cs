namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Represents a distributed-lock acquisition that carries a monotonically increasing
/// fencing token, used to detect and reject writes issued by a stale (preempted or
/// superseded) lock holder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Standard fencing-token usage contract.</b> The protected resource's own write
/// path — never this package — must reject any write presenting a fencing token that is
/// not strictly greater than the last-accepted token it recorded for that resource. This
/// package only supplies the token; it has no knowledge of, and no way to enforce, what
/// "the protected resource" is or how it stores its last-accepted token.
/// </para>
/// <para>
/// Fencing tokens are the industry-standard mitigation for the class of hazard where a
/// lock holder is preempted (GC pause, network partition, a renewal race) and a second
/// holder subsequently acquires the same lock — without a fencing token, the stale first
/// holder can still perform a write that arrives at the protected resource after the
/// second holder's write, silently corrupting state. See Kleppmann's canonical critique
/// of Redlock-style locking for the full rationale.
/// </para>
/// <para>Example:
/// <code>
/// await using var handle = await lockService.AcquireAsync(
///     resource: "invoice:42", expiry, wait, retry, ct);
///
/// if (handle is IFencedLock fenced)
/// {
///     await repository.WriteAsync(data, fencingToken: fenced.FencingToken, ct);
///     // repository.WriteAsync must reject the write if fencingToken is not strictly
///     // greater than the last-accepted token it recorded for "invoice:42".
/// }
/// </code>
/// </para>
/// </remarks>
public interface IFencedLock : IAsyncDisposable
{
    /// <summary>
    /// Gets the monotonically increasing fencing token associated with this lock
    /// acquisition.
    /// </summary>
    /// <value>
    /// Sourced from an atomic, per-resource counter maintained by the lock backend —
    /// never a client-generated GUID or a wall-clock timestamp. Strictly monotonic across
    /// successive successful acquisitions of the same resource; gaps in the sequence
    /// (from failed or contended acquisition attempts) are expected and do not indicate
    /// a defect.
    /// </value>
    long FencingToken { get; }
}
