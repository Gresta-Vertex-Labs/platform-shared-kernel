namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Provides distributed mutual-exclusion locks backed by RedLock.net over Redis.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AcquireAsync"/> returns <see langword="null"/> when the lock cannot be acquired
/// within the <c>wait</c> window. Callers must decide their own fallback strategy —
/// this service never throws for a contended lock; all <see cref="IAsyncDisposable"/> handles
/// are released via <c>await using</c>.
/// </para>
/// <para>
/// Example:
/// <code>
/// await using var handle = await lockService.AcquireAsync(
///     resource : "invoice:42",
///     expiry   : TimeSpan.FromSeconds(30),
///     wait     : TimeSpan.FromSeconds(5),
///     retry    : TimeSpan.FromMilliseconds(200),
///     ct       : cancellationToken);
///
/// if (handle is null)
/// {
///     // lock not acquired within the wait window — implement fallback
///     return;
/// }
/// // critical section
/// </code>
/// </para>
/// </remarks>
public interface IDistributedLockService
{
    /// <summary>
    /// Attempts to acquire a distributed lock on <paramref name="resource"/>.
    /// </summary>
    /// <param name="resource">
    /// A non-empty, globally unique name for the resource being locked
    /// (e.g., <c>"invoice:42"</c>). The consuming service is responsible for namespacing.
    /// </param>
    /// <param name="expiry">
    /// How long the lock is held on Redis before it automatically expires, protecting against
    /// lock-holder crashes. Must be positive.
    /// </param>
    /// <param name="wait">
    /// Maximum time to wait while attempting to acquire the lock before giving up.
    /// Must be non-negative. Pass <see cref="TimeSpan.Zero"/> for a single non-blocking attempt.
    /// </param>
    /// <param name="retry">
    /// Interval between acquisition retries during the <paramref name="wait"/> window.
    /// Must be positive.
    /// </param>
    /// <param name="ct">Cancellation token. Cancelling aborts the acquisition attempt.</param>
    /// <returns>
    /// An <see cref="IAsyncDisposable"/> handle that releases the lock when disposed,
    /// or <see langword="null"/> if the lock could not be acquired within
    /// <paramref name="wait"/>.
    /// </returns>
    Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default);

    /// <summary>
    /// Attempts to acquire a renewable distributed lock on <paramref name="resource"/>.
    /// The returned <see cref="IRenewableLock"/> can be extended via
    /// <see cref="IRenewableLock.RenewAsync"/> to prevent silent expiry during
    /// long-running operations.
    /// </summary>
    /// <param name="resource">
    /// A non-empty, globally unique name for the resource being locked
    /// (e.g., <c>"invoice:42"</c>). The consuming service is responsible for namespacing.
    /// </param>
    /// <param name="expiry">
    /// How long the lock is held on Redis before it automatically expires, protecting against
    /// lock-holder crashes. Must be positive. Call <see cref="IRenewableLock.RenewAsync"/>
    /// before this duration elapses to extend the lock.
    /// </param>
    /// <param name="wait">
    /// Maximum time to wait while attempting to acquire the lock before giving up.
    /// Must be non-negative. Pass <see cref="TimeSpan.Zero"/> for a single non-blocking attempt.
    /// </param>
    /// <param name="retry">
    /// Interval between acquisition retries during the <paramref name="wait"/> window.
    /// Must be positive.
    /// </param>
    /// <param name="ct">Cancellation token. Cancelling aborts the acquisition attempt.</param>
    /// <returns>
    /// An <see cref="IRenewableLock"/> handle that can be renewed and releases the lock
    /// when disposed, or <see langword="null"/> if the lock could not be acquired within
    /// <paramref name="wait"/>. This method never throws for a contended lock.
    /// </returns>
    ValueTask<IRenewableLock?> AcquireRenewableAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default);
}
