using System.ComponentModel;
using System.Data.Common;

namespace SharedKernel.Persistence.Abstractions.Coordination;

/// <summary>
/// A named, exclusive lock scoped to an ALREADY-OPEN ADO.NET transaction — held for the remainder of
/// that transaction's life and released automatically (never explicitly) when it commits or rolls
/// back.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="IMigrationLock"/>: that lock is SESSION-scoped, held by a
/// dedicated connection for an arbitrary caller-controlled duration, and released by an explicit
/// <see cref="IAsyncDisposable.DisposeAsync"/> call — the right shape for "hold this across a whole
/// startup migration run." This lock is TRANSACTION-scoped and self-releasing — the right shape for
/// "serialize concurrent appenders to the same hash chain for the duration of one INSERT," where the
/// caller already has an open transaction (its own, or one it is enlisting in) and wants the lock to
/// disappear exactly when that transaction ends, with no separate release call to forget.
/// </para>
/// <para>
/// The concrete PostgreSQL implementation uses a session-level advisory lock scoped to the transaction
/// (<c>pg_advisory_xact_lock</c>), which PostgreSQL itself releases at <c>COMMIT</c>/<c>ROLLBACK</c> —
/// this interface's shape follows that primitive rather than inventing a release protocol PostgreSQL
/// does not need.
/// </para>
/// <para>Infrastructure seam between the persistence packages; not for application code.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAdvisoryTransactionLock
{
    /// <summary>
    /// Acquires a named exclusive lock scoped to <paramref name="transaction"/>, blocking until it is
    /// available or, when <paramref name="timeout"/> is supplied, until that much time has passed.
    /// </summary>
    /// <param name="connection">The open connection <paramref name="transaction"/> was started on.</param>
    /// <param name="transaction">The open transaction the lock is scoped to.</param>
    /// <param name="lockKey">
    /// The lock's name. Distinct <paramref name="lockKey"/> values never contend with one another.
    /// </param>
    /// <param name="timeout">
    /// The maximum time to wait for the lock, or <see langword="null"/> to wait indefinitely (the
    /// original, still-default behaviour). A caller acquiring this lock from inside a transaction that
    /// itself started server-side idle (e.g. paused awaiting application code before the acquiring
    /// caller resumes) has no other structural bound on how long a second, contending acquirer can be
    /// left waiting — PostgreSQL's own deadlock detector never fires for this shape, since the first
    /// holder's connection is not itself blocked on anything the detector can see. Supplying a timeout
    /// here is the caller's OWN, independent bound; it does not require, and is not replaced by, a
    /// caller separately setting <c>lock_timeout</c> on the same connection before calling this method.
    /// </param>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    /// <exception cref="TimeoutException">
    /// <paramref name="timeout"/> elapsed before the lock could be acquired.
    /// </exception>
    Task AcquireAsync(
        DbConnection connection,
        DbTransaction transaction,
        string lockKey,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
