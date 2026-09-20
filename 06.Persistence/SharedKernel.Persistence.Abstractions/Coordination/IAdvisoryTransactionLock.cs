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
/// </remarks>
public interface IAdvisoryTransactionLock
{
    /// <summary>
    /// Acquires a named exclusive lock scoped to <paramref name="transaction"/>, blocking until it is
    /// available.
    /// </summary>
    /// <param name="connection">The open connection <paramref name="transaction"/> was started on.</param>
    /// <param name="transaction">The open transaction the lock is scoped to.</param>
    /// <param name="lockKey">
    /// The lock's name. Distinct <paramref name="lockKey"/> values never contend with one another.
    /// </param>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    Task AcquireAsync(
        DbConnection connection,
        DbTransaction transaction,
        string lockKey,
        CancellationToken cancellationToken = default);
}
