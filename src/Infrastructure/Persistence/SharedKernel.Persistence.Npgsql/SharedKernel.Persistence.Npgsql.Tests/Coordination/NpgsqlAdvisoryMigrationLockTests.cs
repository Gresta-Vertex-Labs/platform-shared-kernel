using FluentAssertions;
using Npgsql;
using SharedKernel.Persistence.Npgsql.Coordination;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Npgsql.Tests.Coordination;

/// <summary>
/// <see cref="NpgsqlAdvisoryMigrationLock"/> against a real PostgreSQL Testcontainer:
/// acquire/release, cross-instance contention, and timeout.
/// </summary>
public sealed class NpgsqlAdvisoryMigrationLockTests : IAsyncLifetime
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
    public async Task AcquireAsync_ThenDispose_ReleasesTheLock_SoASecondAcquireSucceeds()
    {
        var lockA = new NpgsqlAdvisoryMigrationLock(_dataSource!);
        var lockB = new NpgsqlAdvisoryMigrationLock(_dataSource!);

        await using (var handle = await lockA.AcquireAsync("test-lock-release", TimeSpan.FromSeconds(5)))
        {
            handle.Should().NotBeNull();
        }

        // Session released — a second, independent lock instance must acquire the SAME key promptly.
        await using var secondHandle = await lockB.AcquireAsync("test-lock-release", TimeSpan.FromSeconds(5));
        secondHandle.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_TwoConcurrentRunners_SecondWaitsThenAcquiresAfterFirstReleases()
    {
        var lockA = new NpgsqlAdvisoryMigrationLock(_dataSource!);
        var lockB = new NpgsqlAdvisoryMigrationLock(_dataSource!);

        var firstHandle = await lockA.AcquireAsync("test-lock-contention", TimeSpan.FromSeconds(5));

        var secondAcquireTask = lockB.AcquireAsync("test-lock-contention", TimeSpan.FromSeconds(10));

        // Give the second acquire a genuine chance to observe contention before releasing the first.
        await Task.Delay(500);
        secondAcquireTask.IsCompleted.Should().BeFalse("the second runner must be blocked while the first holds the lock");

        await firstHandle.DisposeAsync();

        await using var secondHandle = await secondAcquireTask;
        secondHandle.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_ContendedBeyondTimeout_ThrowsTimeoutException()
    {
        var lockA = new NpgsqlAdvisoryMigrationLock(_dataSource!);
        var lockB = new NpgsqlAdvisoryMigrationLock(_dataSource!);

        await using var firstHandle = await lockA.AcquireAsync("test-lock-timeout", TimeSpan.FromSeconds(5));

        var act = async () => await lockB.AcquireAsync("test-lock-timeout", TimeSpan.FromMilliseconds(750));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task AcquireAsync_DistinctLockKeys_DoNotContend()
    {
        var lockA = new NpgsqlAdvisoryMigrationLock(_dataSource!);
        var lockB = new NpgsqlAdvisoryMigrationLock(_dataSource!);

        await using var handleA = await lockA.AcquireAsync("test-lock-key-a", TimeSpan.FromSeconds(5));
        await using var handleB = await lockB.AcquireAsync("test-lock-key-b", TimeSpan.FromSeconds(5));

        handleA.Should().NotBeNull();
        handleB.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_HoldsTheMigrationNamespacedKey_NotTheBareName()
    {
        await using var handle = await new NpgsqlAdvisoryMigrationLock(_dataSource!)
            .AcquireAsync("orders-context", TimeSpan.FromSeconds(5));

        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT array_agg((classid::bigint << 32) | objid::bigint) FROM pg_locks WHERE locktype = 'advisory'";
        var keys = (long[]?)await command.ExecuteScalarAsync();

        keys.Should().Contain(AdvisoryLockKeys.ToKey("sk:migration:orders-context"))
            .And.NotContain(AdvisoryLockKeys.ToKey("orders-context"));
    }

    [Fact]
    public async Task AcquireAsync_BareAndNamespacedNames_ContendForTheSameLock()
    {
        await using var handle = await new NpgsqlAdvisoryMigrationLock(_dataSource!)
            .AcquireAsync("orders-context", TimeSpan.FromSeconds(5));

        var act = async () => await new NpgsqlAdvisoryMigrationLock(_dataSource!)
            .AcquireAsync(AdvisoryLockKeys.Migration("orders-context"), TimeSpan.FromMilliseconds(300));

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
