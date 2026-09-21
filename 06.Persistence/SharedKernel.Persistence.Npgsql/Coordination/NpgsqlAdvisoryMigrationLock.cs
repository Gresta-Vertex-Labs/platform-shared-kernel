using System.Diagnostics;
using global::Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Diagnostics;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// PostgreSQL implementation of <see cref="IMigrationLock"/> using a session-level advisory lock
/// (<c>pg_try_advisory_lock</c>/<c>pg_advisory_unlock</c>) held on one dedicated connection.
/// </summary>
/// <remarks>
/// <para>
/// The lock name is namespaced with <see cref="AdvisoryLockKeys.MigrationNamespace"/> before hashing, so
/// it never collides with an application's own advisory locks.
/// </para>
/// <para>
/// A session-level lock needs the same server session for its whole lifetime, which a transaction-mode
/// pooler (PgBouncer) does not guarantee. <c>AddSharedKernelNpgsql</c> therefore builds this lock on the
/// <c>MigrationConnectionString</c> data source when one is configured — point it at the database
/// directly, not at the pooler.
/// </para>
/// <para>
/// The lock is polled with the non-blocking <c>pg_try_advisory_lock</c> until it is acquired or the
/// timeout elapses.
/// </para>
/// </remarks>
public sealed class NpgsqlAdvisoryMigrationLock : IMigrationLock
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlAdvisoryMigrationLock> _logger;

    /// <summary>Initialises a new <see cref="NpgsqlAdvisoryMigrationLock"/>.</summary>
    /// <param name="dataSource">The data source the dedicated lock-holding connection is opened from.</param>
    /// <param name="logger">Optional logger.</param>
    public NpgsqlAdvisoryMigrationLock(
        NpgsqlDataSource dataSource,
        ILogger<NpgsqlAdvisoryMigrationLock>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        _logger = logger ?? NullLogger<NpgsqlAdvisoryMigrationLock>.Instance;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> AcquireAsync(
        string lockKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        var lockName = AdvisoryLockKeys.Migration(lockKey);
        var key = AdvisoryLockKeys.ToKey(lockName);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (await TryAdvisoryLockAsync(connection, key, cancellationToken).ConfigureAwait(false))
                {
                    _logger.AdvisoryMigrationLockAcquired(lockName, stopwatch.ElapsedMilliseconds);
                    return new LockHandle(connection, key, lockName, _logger);
                }

                if (stopwatch.Elapsed >= timeout)
                {
                    _logger.AdvisoryMigrationLockTimedOut(lockName, (long)timeout.TotalMilliseconds);
                    throw new TimeoutException(
                        $"Advisory migration lock '{lockName}' was not acquired within {timeout}.");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<bool> TryAdvisoryLockAsync(
        NpgsqlConnection connection,
        long key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock($1)";
        command.Parameters.Add(new NpgsqlParameter { Value = key });

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private sealed class LockHandle(
        NpgsqlConnection connection,
        long key,
        string lockName,
        ILogger logger) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock($1)";
                command.Parameters.Add(new NpgsqlParameter { Value = key });
                await command.ExecuteScalarAsync().ConfigureAwait(false);
                logger.AdvisoryMigrationLockReleased(lockName);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
