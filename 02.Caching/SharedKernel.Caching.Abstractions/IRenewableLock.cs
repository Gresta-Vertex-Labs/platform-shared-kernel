namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Represents a distributed lock that can be renewed (extended) while held,
/// enabling long-running operations to prevent silent lock expiry.
/// </summary>
/// <remarks>
/// <para>
/// Obtain an instance via
/// <see cref="IDistributedLockService.AcquireRenewableAsync"/>.
/// The caller must <c>await using</c> the handle to ensure the lock is released
/// on completion or exception.
/// </para>
/// <para>
/// <see cref="RenewAsync"/> never throws for a lost lock — it returns
/// <see langword="false"/> when the lock has expired or the underlying store is
/// unavailable. Check <see cref="IsAcquired"/> after each renewal before proceeding
/// with protected work.
/// </para>
/// <para>
/// Use the <c>KeepAliveAsync</c> extension method in <c>SharedKernel.Caching.Redis</c>
/// to automatically renew the lock on a configurable interval in the background.
/// </para>
/// <para>
/// Extends <see cref="IFencedLock"/> — <see cref="IFencedLock.FencingToken"/> reflects the
/// most recent successful acquisition of this lock. A dispose-then-recreate renewal (see
/// <see cref="RenewAsync"/>) issues a fresh token on every successful renewal, so the token
/// observed after a renewal is always strictly greater than the token observed before it.
/// </para>
/// <para>Example:
/// <code>
/// await using var renewableLock = await lockService.AcquireRenewableAsync(
///     resource : "invoice:42",
///     expiry   : TimeSpan.FromSeconds(30),
///     wait     : TimeSpan.FromSeconds(5),
///     retry    : TimeSpan.FromMilliseconds(200),
///     ct       : cancellationToken);
///
/// if (renewableLock is null)
///     return; // lock not acquired within wait window
///
/// // Manually renew — or use KeepAliveAsync for automatic background renewal.
/// bool stillHeld = await renewableLock.RenewAsync(cancellationToken);
/// if (!stillHeld)
///     throw new InvalidOperationException("Lock expired before renewal.");
/// </code>
/// </para>
/// </remarks>
public interface IRenewableLock : IFencedLock
{
    /// <summary>
    /// Gets a value indicating whether this lock is currently held.
    /// </summary>
    /// <value>
    /// <see langword="true"/> while the lock is acquired;
    /// <see langword="false"/> after the lock has been released via
    /// <see cref="IAsyncDisposable.DisposeAsync"/>, or after
    /// <see cref="RenewAsync"/> returned <see langword="false"/>.
    /// </value>
    bool IsAcquired { get; }

    /// <summary>
    /// Attempts to extend the lifetime of the distributed lock.
    /// </summary>
    /// <param name="ct">Cancellation token. Cancelling aborts the renewal attempt.</param>
    /// <returns>
    /// <see langword="true"/> if the lock was successfully renewed and is still held;
    /// <see langword="false"/> if the lock expired before renewal, the underlying store
    /// was unavailable, or the lock was already released. This method never throws for
    /// a lost lock — all failure conditions are absorbed and returned as
    /// <see langword="false"/>.
    /// </returns>
    ValueTask<bool> RenewAsync(CancellationToken ct = default);
}
