using System.Data.Common;
using Npgsql;
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
/// <para>
/// <strong>Self-deadlock hazard this class cannot see, and <paramref name="timeout"/>-less callers
/// remain exposed to:</strong> two acquirers contending for the SAME <paramref name="lockKey"/> on two
/// DIFFERENT, already-open transactions never form the cycle PostgreSQL's own deadlock detector looks
/// for when the FIRST holder's connection is idle-in-transaction awaiting application code (rather than
/// itself blocked on a database wait) — the detector sees no cycle, and the second acquirer can wait
/// forever. Passing <c>timeout</c> to <see cref="AcquireAsync"/> is this class's own, structural bound
/// against that shape; a caller that omits it keeps today's unbounded-wait behaviour exactly, and must
/// supply its own bound some other way (e.g. its own <c>lock_timeout</c> session setting) if it needs one.
/// </para>
/// </remarks>
public sealed class NpgsqlAdvisoryTransactionLock : IAdvisoryTransactionLock
{
    /// <inheritdoc />
    /// <remarks>
    /// When <paramref name="timeout"/> is supplied, binds PostgreSQL's <c>lock_timeout</c> GUC
    /// transaction-locally (<c>SELECT set_config('lock_timeout', ..., true)</c> — the same
    /// parameterized <c>set_config</c> shape <c>NpgsqlTenantSessionBinder</c> uses, never a literal
    /// interpolated into <c>SET LOCAL</c>, which cannot be parameterized at all) immediately before
    /// attempting to acquire the lock, then translates the resulting <c>55P03</c>
    /// (<c>lock_not_available</c>) <see cref="PostgresException"/> into a <see cref="TimeoutException"/>.
    /// This is independent of, and composes with, any <c>lock_timeout</c> a caller already set on the
    /// same connection for its own purposes — this method's own binding simply overwrites it for the
    /// duration of the acquisition attempt, transaction-locally, exactly like every other transaction-
    /// scoped <c>set_config</c> call in this codebase.
    /// </remarks>
    public async Task AcquireAsync(
        DbConnection connection,
        DbTransaction transaction,
        string lockKey,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        if (timeout is { } requestedTimeout && requestedTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout), requestedTimeout,
                "A zero or negative timeout would set PostgreSQL's 'lock_timeout' to 0, which means " +
                    "'wait forever' — the opposite of what a bounded wait is asking for. Pass null " +
                    "for an unbounded wait, or a positive duration for a real bound.");
        }

        if (timeout is { } boundedWait)
            await BindLockTimeoutAsync(connection, transaction, boundedWait, cancellationToken).ConfigureAwait(false);

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

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (timeout is not null && ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new TimeoutException(
                $"Could not acquire the advisory transaction lock '{lockKey}' within {timeout}.", ex);
        }
    }

    private static async Task BindLockTimeoutAsync(
        DbConnection connection, DbTransaction transaction, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config('lock_timeout', $1, true)";

        var parameter = command.CreateParameter();
        parameter.Value = $"{(int)timeout.TotalMilliseconds}ms";
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
