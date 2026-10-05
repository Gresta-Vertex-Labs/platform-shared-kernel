namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Cross-process mutual exclusion: locks held until released, and leases that expire on their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lock</b> (<see cref="TryAcquireAsync"/>): guards a critical section. The implementation keeps
/// the lock alive while it is held and releases it on <see cref="IAsyncDisposable.DisposeAsync"/>.
/// Always <c>await using</c> the handle.
/// </para>
/// <para>
/// <b>Lease</b> (<see cref="TryAcquireLeaseAsync"/>): claims a resource for a fixed time and is never
/// released or extended, for example "only one replica runs this 02:00 occurrence of a job".
/// </para>
/// <para>
/// <b>Outcomes.</b> Both methods return <see langword="null"/> only when another holder has the
/// resource. When the lock store cannot be reached they throw
/// <see cref="DistributedLockUnavailableException"/>, so an outage is never mistaken for contention.
/// </para>
/// <para>
/// <b>Correctness.</b> A holder can lose a lock without knowing it, for example during a long GC
/// pause. Pass <see cref="IDistributedLock.FencingToken"/> or <see cref="DistributedLease.FencingToken"/>
/// to the protected resource and have it reject a token that is not greater than the last one it
/// accepted. Resource names are global across services; prefix them with your service name unless
/// several services must contend for the same resource.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Lock: guard a critical section; released on dispose.
/// await using IDistributedLock? handle = await locks.TryAcquireAsync(
///     "billing:invoice:42",
///     new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30), WaitTime = TimeSpan.FromSeconds(5) },
///     ct);
/// if (handle is null)
///     return; // another holder kept it for the whole wait time
///
/// using var work = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.LostToken);
/// await invoices.SettleAsync(42, handle.FencingToken, work.Token);
///
/// // Lease: claim a one-off occurrence across replicas; never released.
/// DistributedLease? lease = await locks.TryAcquireLeaseAsync("reports:nightly:2026-09-17", TimeSpan.FromHours(1), ct);
/// if (lease is null)
///     return; // another replica claimed this run
/// </code>
/// </example>
public interface IDistributedLockService
{
    /// <summary>Tries to acquire a lock on <paramref name="resource"/>.</summary>
    /// <param name="resource">The resource name. Must not be null or whitespace.</param>
    /// <param name="options">Expiry, wait and retry settings, or <see langword="null"/> for <see cref="DistributedLockOptions.Default"/>.</param>
    /// <param name="ct">Cancellation token. Cancelling aborts the attempt.</param>
    /// <returns>The held lock, or <see langword="null"/> when another holder kept the resource for the whole wait time.</returns>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is null or whitespace.</exception>
    /// <exception cref="DistributedLockUnavailableException">The lock store could not be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    ValueTask<IDistributedLock?> TryAcquireAsync(
        string resource,
        DistributedLockOptions? options = null,
        CancellationToken ct = default);

    /// <summary>Tries to claim <paramref name="resource"/> for <paramref name="duration"/>, in a single attempt.</summary>
    /// <param name="resource">The resource name. Must not be null or whitespace.</param>
    /// <param name="duration">How long the claim lasts. Must be positive.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lease, or <see langword="null"/> when the resource is already claimed.</returns>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is not positive.</exception>
    /// <exception cref="DistributedLockUnavailableException">The lock store could not be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    ValueTask<DistributedLease?> TryAcquireLeaseAsync(
        string resource,
        TimeSpan duration,
        CancellationToken ct = default);
}
