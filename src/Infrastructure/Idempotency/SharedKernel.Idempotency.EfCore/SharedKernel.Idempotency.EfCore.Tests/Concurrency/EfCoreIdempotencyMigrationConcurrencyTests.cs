using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Tests.Support;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// Replicas starting at once against one empty database. Real PostgreSQL: the migration lock and the atomic upsert are
/// what is under test, so nothing here is mocked.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON.</remarks>
[Collection("PostgreSqlContainer")]
public sealed class EfCoreIdempotencyMigrationConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private const string HistoryTable = "__ef_migrations_history_idempotency";

    [Fact]
    public async Task TwoHostsStartingTogether_RunTheMigrationOnce_AndTheStoreIsAtomicAfterwards()
    {
        await using var db = await fixture.Server.CreateDatabaseAsync();
        await using var first = MigrationHost.Create(db, o => o.MigrateOnStartup = true);
        await using var second = MigrationHost.Create(db, o => o.MigrateOnStartup = true);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = new[] { first, second }.Select(host => Task.Run(async () =>
        {
            await gate.Task;
            await host.StartAsync();
        })).ToArray();
        gate.SetResult();
        await Task.WhenAll(starts);

        Assert.Equal(1, await Sql.HistoryCountAsync(db.AdminConnectionString, HistoryTable));
        Assert.Equal(1, first.Commands.Count("to_regclass('idempotency_keys')") + second.Commands.Count("to_regclass('idempotency_keys')"));

        // N genuinely concurrent TryBeginAsync on one key through both hosts: exactly one Started.
        const int concurrency = 32;
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
        {
            await begin.Task;
            return await (i % 2 == 0 ? first : second).BeginAsync("k-race");
        })).ToArray();
        begin.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.Single(results, r => r.Status == IdempotencyReservationStatus.Started);
        Assert.Equal(concurrency - 1, results.Count(r => r.Status == IdempotencyReservationStatus.InProgress));
    }
}
