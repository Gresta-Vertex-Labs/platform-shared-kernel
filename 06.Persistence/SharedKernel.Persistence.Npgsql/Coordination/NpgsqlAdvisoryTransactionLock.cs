using System.Data.Common;
using System.Globalization;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Coordination;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// PostgreSQL implementation of <see cref="IAdvisoryTransactionLock"/>: <c>pg_advisory_xact_lock</c>,
/// released by PostgreSQL itself when the transaction commits or rolls back.
/// </summary>
/// <remarks>
/// <para>
/// The lock name is hashed with <see cref="AdvisoryLockKeys.ToKey"/> as given; pass a namespaced name
/// (e.g. <see cref="AdvisoryLockKeys.Audit"/>).
/// </para>
/// <para>
/// <strong>Bounded wait.</strong> With a timeout, the transaction's <c>lock_timeout</c> is set for the
/// acquisition only and restored to its previous value afterwards, so later statements of the same
/// transaction keep their own timeout. A sub-millisecond timeout is rounded up to 1 ms
/// (<c>lock_timeout = 0</c> would mean "wait forever"). A timed-out acquisition raises SQLSTATE 55P03,
/// which aborts the transaction; it surfaces as <see cref="TimeoutException"/>.
/// </para>
/// </remarks>
public sealed class NpgsqlAdvisoryTransactionLock : IAdvisoryTransactionLock
{
    // Reads the previous value before replacing it: the MATERIALIZED CTE is evaluated first.
    private const string SetTimeoutSql =
        "WITH previous AS MATERIALIZED (SELECT current_setting('lock_timeout') AS value) "
        + "SELECT previous.value, set_config('lock_timeout', @timeout, true) FROM previous";

    // One round trip: acquire, then restore. PostgreSQL skips the restore when the acquisition fails.
    private const string AcquireAndRestoreSql =
        "SELECT pg_advisory_xact_lock(@key); SELECT set_config('lock_timeout', @previous, true)";

    private const string AcquireSql = "SELECT pg_advisory_xact_lock(@key)";

    /// <inheritdoc />
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

        if (timeout is { } requested && requested <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout), requested,
                "A timeout must be positive; pass null to wait without a bound.");
        }

        var key = AdvisoryLockKeys.ToKey(lockKey);

        if (timeout is not { } boundedWait)
        {
            await using var acquire = CreateCommand(connection, transaction, AcquireSql, ("key", key));
            await acquire.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var previous = await SetLockTimeoutAsync(connection, transaction, boundedWait, cancellationToken).ConfigureAwait(false);

        await using var command = CreateCommand(
            connection, transaction, AcquireAndRestoreSql, ("key", key), ("previous", previous));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new TimeoutException(
                $"Could not acquire the advisory transaction lock '{lockKey}' within {boundedWait}.", ex);
        }
    }

    /// <summary>The <c>lock_timeout</c> value for <paramref name="timeout"/>, never below 1 ms.</summary>
    internal static string ToLockTimeout(TimeSpan timeout)
    {
        var milliseconds = Math.Max(1L, (long)Math.Ceiling(timeout.TotalMilliseconds));
        milliseconds = Math.Min(milliseconds, int.MaxValue);
        return milliseconds.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    private static async Task<string> SetLockTimeoutAsync(
        DbConnection connection, DbTransaction transaction, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, SetTimeoutSql, ("timeout", ToLockTimeout(timeout)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return reader.GetString(0);
    }

    // Named parameters: the acquire-and-restore command holds two statements, which positional
    // placeholders cannot span.
    private static DbCommand CreateCommand(
        DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in values)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
