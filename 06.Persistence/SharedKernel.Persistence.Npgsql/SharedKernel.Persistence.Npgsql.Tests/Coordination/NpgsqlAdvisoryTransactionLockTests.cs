using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Coordination;

/// <summary>
/// <see cref="NpgsqlAdvisoryTransactionLock"/> against a real PostgreSQL
/// Testcontainer: genuine blocking contention on the same key, no contention across distinct keys,
/// and automatic release on both commit and rollback (<c>pg_advisory_xact_lock</c> needs no explicit
/// unlock call — PostgreSQL releases it itself at transaction end either way).
/// </summary>
public sealed class NpgsqlAdvisoryTransactionLockTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_NullConnection_ThrowsArgumentNullException()
    {
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () => await advisoryLock.AcquireAsync(null!, null!, "k");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AcquireAsync_NullOrWhiteSpaceLockKey_ThrowsArgumentException()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () => await advisoryLock.AcquireAsync(connection, transaction, " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AcquireAsync_WithinOneTransaction_Succeeds()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () => await advisoryLock.AcquireAsync(connection, transaction, "xact-lock-basic");

        await act.Should().NotThrowAsync();
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_SecondTransactionSameKey_BlocksUntilFirstCommits()
    {
        const string lockKey = "xact-lock-contention";
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using var connectionA = await _dataSource!.OpenConnectionAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await advisoryLock.AcquireAsync(connectionA, transactionA, lockKey);

        await using var connectionB = await _dataSource!.OpenConnectionAsync();
        await using var transactionB = await connectionB.BeginTransactionAsync();
        var secondAcquireTask = advisoryLock.AcquireAsync(connectionB, transactionB, lockKey);

        // Give the second acquire a genuine chance to observe contention before releasing the first.
        await Task.Delay(500);
        secondAcquireTask.IsCompleted.Should().BeFalse(
            "pg_advisory_xact_lock blocks until the first transaction holding the same key ends");

        await transactionA.CommitAsync();

        await secondAcquireTask;
        secondAcquireTask.IsCompletedSuccessfully.Should().BeTrue();
        await transactionB.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_SecondTransactionSameKey_AlsoUnblocksAfterFirstRollsBack()
    {
        const string lockKey = "xact-lock-rollback-release";
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using var connectionA = await _dataSource!.OpenConnectionAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await advisoryLock.AcquireAsync(connectionA, transactionA, lockKey);

        await using var connectionB = await _dataSource!.OpenConnectionAsync();
        await using var transactionB = await connectionB.BeginTransactionAsync();
        var secondAcquireTask = advisoryLock.AcquireAsync(connectionB, transactionB, lockKey);

        await Task.Delay(500);
        secondAcquireTask.IsCompleted.Should().BeFalse();

        await transactionA.RollbackAsync();

        await secondAcquireTask;
        secondAcquireTask.IsCompletedSuccessfully.Should().BeTrue();
        await transactionB.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_DistinctLockKeys_DoNotContend()
    {
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using var connectionA = await _dataSource!.OpenConnectionAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await using var connectionB = await _dataSource!.OpenConnectionAsync();
        await using var transactionB = await connectionB.BeginTransactionAsync();

        var actA = async () => await advisoryLock.AcquireAsync(connectionA, transactionA, "xact-lock-key-a");
        var actB = async () => await advisoryLock.AcquireAsync(connectionB, transactionB, "xact-lock-key-b");

        await actA.Should().NotThrowAsync();
        await actB.Should().NotThrowAsync();

        await transactionA.CommitAsync();
        await transactionB.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_ZeroTimeout_ThrowsArgumentOutOfRangeException()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () => await advisoryLock.AcquireAsync(connection, transaction, "xact-lock-zero-timeout", TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>(
            "PostgreSQL's lock_timeout=0 means 'wait forever', the opposite of a bounded wait");
    }

    [Fact]
    public async Task AcquireAsync_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () =>
            await advisoryLock.AcquireAsync(connection, transaction, "xact-lock-negative-timeout", TimeSpan.FromSeconds(-1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AcquireAsync_WithTimeout_LockImmediatelyAvailable_StillSucceeds()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        var act = async () =>
            await advisoryLock.AcquireAsync(connection, transaction, "xact-lock-timeout-uncontended", TimeSpan.FromSeconds(5));

        await act.Should().NotThrowAsync("a timeout must never interfere with an uncontended acquisition");
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_SecondTransactionSameKey_WithTimeout_ThrowsTimeoutException_WithoutWaitingForFirstToCommit()
    {
        // The exact self-deadlock shape a timeout-less caller has no structural defence against: the
        // FIRST holder never commits during this test (simulating a transaction idle-in-transaction,
        // awaiting application code) — PostgreSQL's own deadlock detector never fires for this shape,
        // because the first connection is not itself blocked on anything. Only the SECOND acquirer's
        // own timeout bounds the wait.
        const string lockKey = "xact-lock-timeout-contention";
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using var connectionA = await _dataSource!.OpenConnectionAsync();
        await using var transactionA = await connectionA.BeginTransactionAsync();
        await advisoryLock.AcquireAsync(connectionA, transactionA, lockKey);

        await using var connectionB = await _dataSource!.OpenConnectionAsync();
        await using var transactionB = await connectionB.BeginTransactionAsync();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var act = async () => await advisoryLock.AcquireAsync(connectionB, transactionB, lockKey, TimeSpan.FromMilliseconds(500));

        (await act.Should().ThrowAsync<TimeoutException>())
            .Which.InnerException.Should().BeOfType<PostgresException>();
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(10), "the timeout must actually bound the wait, not merely be accepted and ignored");

        // The first holder's transaction was never touched by the second's failed attempt — it can
        // still commit normally afterward.
        await transactionA.CommitAsync();
        await transactionB.RollbackAsync();
    }

    [Fact]
    public async Task AcquireAsync_ThirdTransaction_AcquiresImmediately_AfterEarlierHolderCommits()
    {
        const string lockKey = "xact-lock-sequential-reuse";
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using (var connectionA = await _dataSource!.OpenConnectionAsync())
        await using (var transactionA = await connectionA.BeginTransactionAsync())
        {
            await advisoryLock.AcquireAsync(connectionA, transactionA, lockKey);
            await transactionA.CommitAsync();
        }

        await using var connectionC = await _dataSource!.OpenConnectionAsync();
        await using var transactionC = await connectionC.BeginTransactionAsync();
        var act = async () => await advisoryLock.AcquireAsync(connectionC, transactionC, lockKey);

        await act.Should().NotThrowAsync("the earlier holder already committed, releasing the key");
        await transactionC.CommitAsync();
    }

    // A30: the bounded wait used to leave lock_timeout set for the rest of the transaction.
    [Fact]
    public async Task AcquireAsync_WithTimeout_RestoresTheTransactionsPreviousLockTimeout()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, transaction, "SET LOCAL lock_timeout = '7s'");

        await new NpgsqlAdvisoryTransactionLock().AcquireAsync(
            connection, transaction, "xact-lock-restore", TimeSpan.FromMilliseconds(250));

        (await ScalarAsync(connection, transaction, "SHOW lock_timeout")).Should().Be("7s");
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task AcquireAsync_WithTimeout_NoPreviousValue_RestoresTheServerDefault()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        var before = await ScalarAsync(connection, null, "SHOW lock_timeout");
        await using var transaction = await connection.BeginTransactionAsync();

        await new NpgsqlAdvisoryTransactionLock().AcquireAsync(
            connection, transaction, "xact-lock-restore-default", TimeSpan.FromSeconds(3));

        (await ScalarAsync(connection, transaction, "SHOW lock_timeout")).Should().Be(before);
        await transaction.CommitAsync();
    }

    [Theory]
    [InlineData(0.2, "1ms")]
    [InlineData(1, "1ms")]
    [InlineData(1.5, "2ms")]
    [InlineData(2500, "2500ms")]
    public void ToLockTimeout_NeverRoundsToZero(double milliseconds, string expected)
    {
        NpgsqlAdvisoryTransactionLock.ToLockTimeout(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Fact]
    public async Task AcquireAsync_SubMillisecondTimeout_OnAHeldLock_TimesOutInsteadOfWaitingForever()
    {
        const string lockKey = "xact-lock-sub-ms";
        var advisoryLock = new NpgsqlAdvisoryTransactionLock();

        await using var holderConnection = await _dataSource!.OpenConnectionAsync();
        await using var holder = await holderConnection.BeginTransactionAsync();
        await advisoryLock.AcquireAsync(holderConnection, holder, lockKey);

        await using var waiterConnection = await _dataSource!.OpenConnectionAsync();
        await using var waiter = await waiterConnection.BeginTransactionAsync();
        var act = async () => await advisoryLock.AcquireAsync(
            waiterConnection, waiter, lockKey, TimeSpan.FromTicks(1_000)); // 0.1 ms

        await act.Should().ThrowAsync<TimeoutException>().WaitAsync(TimeSpan.FromSeconds(10));
        await holder.RollbackAsync();
    }

    [Fact]
    public async Task AcquireAsync_HashesTheNameAsGiven_SoANamespacedAuditLockUsesItsNamespacedKey()
    {
        var lockName = AdvisoryLockKeys.Audit("chain-a");
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await new NpgsqlAdvisoryTransactionLock().AcquireAsync(connection, transaction, lockName);

        var held = await ScalarAsync(
            connection, transaction,
            $"SELECT count(*)::text FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid() "
                + $"AND ((classid::bigint << 32) | objid::bigint) = {AdvisoryLockKeys.ToKey(lockName)}");
        held.Should().Be("1");
        await transaction.CommitAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (string?)await command.ExecuteScalarAsync();
    }
}
