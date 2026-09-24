using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Integration;

/// <summary>
/// P-558 (E2) against real PostgreSQL: the expected-version update (ETag / If-Match) over xmin, and the
/// soft-delete-aware bulk delete (A5) plus explicit purge.
/// </summary>
[Collection("PostgreSQL")]
public sealed class RepositoryPostgresTests(PostgreSqlContainerFixture fixture)
{
    private const string DatabaseName = "sk_persistence_concurrency";

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private ConcurrencyTestDbContext CreateContext(IClock? clock = null)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgres(TestNpgsqlDataSources.Get(ConnectionString));
        clock ??= new FakeClock();

        return new ConcurrencyTestDbContext(
            builder.Options,
            PersistenceContextDependencies.Create(
                requestContext: new FakeAuditActorContext(), clock: clock, entityVersionKeys: TestEntityVersionKeys.Provider));
    }

    private async Task<(ConcurrentPgId Id, EntityVersion Version)> SeedAsync(string name)
    {
        var id = ConcurrentPgId.New();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var aggregate = new ConcurrentPgAggregate(id, name, new SystemClock());
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();
        return (id, ConcurrencyVersion.Get(context, aggregate));
    }

    [Fact]
    public async Task UpdateAsync_WithTheCurrentVersion_Saves()
    {
        var (id, version) = await SeedAsync("Original");

        await using var context = CreateContext();
        var repo = new EfRepository<ConcurrentPgAggregate, ConcurrentPgId>(context);
        var aggregate = await repo.GetByIdAsync(id);
        aggregate!.Rename("Renamed");

        await repo.UpdateAsync(aggregate, version);
        await context.SaveChangesAsync();

        await using var verify = CreateContext();
        (await verify.Aggregates.AsNoTracking().SingleAsync(a => a.Id == id)).Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task UpdateAsync_WithAStaleVersion_FailsWithConflict_TrackedAndDetached()
    {
        var (id, staleVersion) = await SeedAsync("Original");

        // Another writer changes the row, moving xmin on.
        await using (var other = CreateContext())
        {
            (await other.Aggregates.SingleAsync(a => a.Id == id)).Rename("Other writer");
            await other.SaveChangesAsync();
        }

        // Tracked: the aggregate is loaded fresh, but the client's If-Match carries the old version.
        await using (var context = CreateContext())
        {
            var repo = new EfRepository<ConcurrentPgAggregate, ConcurrentPgId>(context);
            var aggregate = await repo.GetByIdAsync(id);
            aggregate!.Rename("Client edit");
            await repo.UpdateAsync(aggregate, staleVersion);

            await FluentActions.Awaiting(() => context.SaveChangesAsync()).Should().ThrowAsync<ConflictException>();
        }

        // Detached: a snapshot read earlier is attached with the version it was read at.
        ConcurrentPgAggregate snapshot;
        await using (var read = CreateContext())
            snapshot = await new EfReadRepository<ConcurrentPgAggregate, ConcurrentPgId>(read).GetByIdAsync(id) ?? throw new InvalidOperationException();

        await using (var write = CreateContext())
        {
            (await write.Aggregates.SingleAsync(a => a.Id == id)).Rename("Third writer");
            await write.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repo = new EfRepository<ConcurrentPgAggregate, ConcurrentPgId>(context);
            snapshot.Rename("Detached edit");

            // An IHasConcurrency aggregate carries its row version itself, so its version can be read detached.
            await repo.UpdateAsync(snapshot, ConcurrencyVersion.Get(context, snapshot));

            await FluentActions.Awaiting(() => context.SaveChangesAsync()).Should().ThrowAsync<ConflictException>();
        }

        await using var verify = CreateContext();
        (await verify.Aggregates.AsNoTracking().SingleAsync(a => a.Id == id)).Name.Should().Be("Third writer");
    }

    [Fact]
    public async Task ExecuteDeleteAsync_SoftDeletes_AndExecutePurgeAsync_Removes()
    {
        var marker = $"bulk-{Guid.NewGuid():N}";
        var (first, _) = await SeedAsync(marker);
        var (second, _) = await SeedAsync(marker);
        var now = new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero);

        await using var context = CreateContext(new FakeClock(now));
        var repo = new EfRepository<ConcurrentPgAggregate, ConcurrentPgId>(context);

        (await repo.ExecuteDeleteAsync(Spec.For<ConcurrentPgAggregate>().Where(a => a.Name == marker))).Should().Be(2);

        var rows = await context.Aggregates.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Id == first || a.Id == second).ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(a => a.IsDeleted && a.DeletedOn == now && a.DeletedBy != null);

        (await repo.ExecutePurgeAsync(Spec.For<ConcurrentPgAggregate>().Where(a => a.Name == marker).IncludeDeleted())).Should().Be(2);
        (await context.Aggregates.IgnoreQueryFilters().CountAsync(a => a.Name == marker)).Should().Be(0);
    }
}
