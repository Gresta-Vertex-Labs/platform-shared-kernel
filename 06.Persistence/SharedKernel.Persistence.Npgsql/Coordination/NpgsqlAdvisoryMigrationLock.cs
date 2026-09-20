using System.Diagnostics;
using global::Npgsql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Npgsql.Diagnostics;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// PostgreSQL implementation of <see cref="IMigrationLock"/> using session-level advisory locks
/// (<c>pg_try_advisory_lock</c>/<c>pg_advisory_unlock</c>).
/// </summary>
/// <remarks>
/// <para>
/// A session-level advisory lock is held by ONE dedicated
/// connection for the lock's whole lifetime — it is released either explicitly
/// (<c>pg_advisory_unlock</c>) or implicitly when that connection closes. This implementation opens
/// a fresh connection per <see cref="AcquireAsync"/> call, outside the shared
/// <see cref="NpgsqlDataSource"/> pool's normal open/close-per-operation lifecycle, and keeps it open
/// for the returned handle's entire lifetime — releasing and closing it together in
/// <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </para>
/// <para>
/// <c>pg_advisory_lock</c> (the blocking form) has no built-in timeout, so this implementation polls
/// <c>pg_try_advisory_lock</c> (the non-blocking form) at a short fixed interval until either the lock
/// is acquired or <paramref name="timeout"/> in <see cref="AcquireAsync"/> elapses.
/// </para>
/// <para>
/// <see cref="AcquireAsync"/>'s <c>lockKey</c> string is hashed deterministically to the
/// <see cref="long"/> key PostgreSQL's advisory-lock functions require — the same string always
/// produces the same key across process restarts and replicas, which is the whole point of a
/// cross-replica coordination lock.
/// </para>
/// </remarks>
public sealed class NpgsqlAdvisoryMigrationLock : IMigrationLock
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlAdvisoryMigrationLock> _logger;

    /// <summary>Initialises a new <see cref="NpgsqlAdvisoryMigrationLock"/>.</summary>
    /// <param name="dataSource">The data source a dedicated lock-holding connection is opened from.</param>
    /// <param name="logger">Optional logger. Falls back to <see cref="NullLogger{T}"/>.</param>
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

        long key = AdvisoryLockKeyHasher.Compute(lockKey);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (await TryAdvisoryLockAsync(connection, key, cancellationToken))
                {
                    _logger.AdvisoryMigrationLockAcquired(lockKey, stopwatch.ElapsedMilliseconds);
                    return new NpgsqlAdvisoryLockHandle(connection, key, lockKey, _logger);
                }

                if (stopwatch.Elapsed >= timeout)
                {
                    _logger.AdvisoryMigrationLockTimedOut(lockKey, (long)timeout.TotalMilliseconds);
                    throw new TimeoutException(
                        $"Advisory migration lock '{lockKey}' was not acquired within {timeout}.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }
        catch
        {
            await connection.DisposeAsync();
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

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool acquired && acquired;
    }

    private sealed class NpgsqlAdvisoryLockHandle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _key;
        private readonly string _lockKey;
        private readonly ILogger _logger;
        private bool _disposed;

        public NpgsqlAdvisoryLockHandle(
            NpgsqlConnection connection,
            long key,
            string lockKey,
            ILogger logger)
        {
            _connection = connection;
            _key = key;
            _lockKey = lockKey;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await using var command = _connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock($1)";
                command.Parameters.Add(new NpgsqlParameter { Value = _key });
                await command.ExecuteScalarAsync();
                _logger.AdvisoryMigrationLockReleased(_lockKey);
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
