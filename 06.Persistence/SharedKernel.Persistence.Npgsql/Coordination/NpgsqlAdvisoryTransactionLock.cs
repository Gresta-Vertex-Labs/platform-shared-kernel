using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Coordination;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// PostgreSQL implementation of <see cref="IAdvisoryTransactionLock"/> using a transaction-scoped
/// advisory lock (<c>pg_advisory_xact_lock</c>) — released automatically by PostgreSQL itself when
/// <paramref name="transaction"/> commits or rolls back.
/// </summary>
/// <remarks>
/// <para>
/// <c>pg_advisory_xact_lock</c> (unlike <c>pg_advisory_lock</c>, the session-level form
/// <c>NpgsqlAdvisoryMigrationLock</c> uses) BLOCKS until the lock is available and needs no separate
/// release call — the correct primitive for "serialize concurrent appenders to the same audit chain for
/// the life of one already-open transaction," see <see cref="IAdvisoryTransactionLock"/>'s remarks for
/// the full comparison.
/// </para>
/// <para>
/// Derives its <c>bigint</c> lock key via <see cref="AdvisoryLockKeyHasher"/>, shared with
/// <c>NpgsqlAdvisoryMigrationLock</c> — a session-level lock and a transaction-level lock occupy the
/// SAME <c>pg_locks</c> advisory keyspace, so both must compute the identical key for the same logical
/// resource name.
/// </para>
/// </remarks>
public sealed class NpgsqlAdvisoryTransactionLock : IAdvisoryTransactionLock
{
    /// <inheritdoc />
    public async Task AcquireAsync(
        DbConnection connection,
        DbTransaction transaction,
        string lockKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock($1)";

        // Deliberately anonymous (no ParameterName set) — Npgsql interprets "$1" in the SQL text as
        // "the first parameter added, positionally," the same pattern NpgsqlAdvisoryMigrationLock's
        // own working pg_try_advisory_lock($1) call uses. Explicitly naming it "$1" (a real, earlier
        // version of this method) makes Npgsql treat it as a NAMED parameter that never matches the
        // positional placeholder, producing "bind message supplies 0 parameters, but prepared
        // statement requires 1" — caught by the Postgres-backed test suite, not by inspection.
        var parameter = command.CreateParameter();
        parameter.Value = AdvisoryLockKeyHasher.Compute(lockKey);
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
