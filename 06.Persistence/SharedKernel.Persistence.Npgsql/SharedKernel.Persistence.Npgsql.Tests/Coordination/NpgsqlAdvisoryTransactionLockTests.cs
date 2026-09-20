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
}
