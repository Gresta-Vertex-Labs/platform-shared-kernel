using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using System.Data.Common;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

/// <summary>Counts every SQL statement executed via <c>ExecuteReader(Async)</c> — a round-trip counter.</summary>
internal sealed class RoundTripCountingInterceptor : DbCommandInterceptor
{
    public int ReaderCommandCount { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        ReaderCommandCount++;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ReaderCommandCount++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// <see cref="EfReadRepository{TAggregate,TId}.GetByIdsChunkedAsync"/> tests.
/// </summary>
public sealed class GetByIdsChunkedAsyncTests
{
    private static (TestDbContext Ctx, RoundTripCountingInterceptor Counter) CreateContextWithCounter()
    {
        var counter = new RoundTripCountingInterceptor();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .AddInterceptors(counter)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        var audit = PersistenceContextDependencies.Create(userContext, clock);

        var ctx = new TestDbContext(options, audit);
        ctx.Database.EnsureCreated();
        return (ctx, counter);
    }

    private sealed class ChunkedTestReadRepo(TestDbContext ctx)
        : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>());

    [Fact]
    public async Task GetByIdsChunkedAsync_ChunkSizeSmallerThanN_IssuesCeilNOverChunkSizeRoundTrips_ReturnsFullSet()
    {
        // Arrange — 7 ids, chunkSize 3 -> ceil(7/3) = 3 round trips.
        var (ctx, counter) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        var ids = new List<TestId>();
        for (var i = 0; i < 7; i++)
        {
            var id = TestId.New();
            ids.Add(id);
            ctx.TestAggregates.Add(new TestAggregate(id, $"Item{i}", new SystemClock()));
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        // Baseline AFTER seeding — SQLite's provider executes INSERT statements via ExecuteReader
        // (RETURNING-based), so the seed inserts themselves inflate ReaderCommandCount; only the
        // DELTA caused by GetByIdsChunkedAsync itself is meaningful.
        var baseline = counter.ReaderCommandCount;

        // Act
        var result = await repo.GetByIdsChunkedAsync(ids, chunkSize: 3);

        // Assert
        result.Should().HaveCount(7);
        result.Select(a => a.Id).Should().BeEquivalentTo(ids);
        (counter.ReaderCommandCount - baseline).Should().Be(3, "ceil(7/3) = 3 round trips expected");
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_ChunkSizeGreaterOrEqualToN_IssuesExactlyOneRoundTrip()
    {
        var (ctx, counter) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        var ids = new List<TestId>();
        for (var i = 0; i < 4; i++)
        {
            var id = TestId.New();
            ids.Add(id);
            ctx.TestAggregates.Add(new TestAggregate(id, $"Item{i}", new SystemClock()));
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var baseline = counter.ReaderCommandCount;

        var result = await repo.GetByIdsChunkedAsync(ids, chunkSize: 10);

        result.Should().HaveCount(4);
        (counter.ReaderCommandCount - baseline).Should().Be(1, "chunkSize >= N must issue exactly one round trip");
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_EmptyInput_ReturnsEmptyList_NoRoundTrips()
    {
        var (ctx, counter) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        var result = await repo.GetByIdsChunkedAsync([], chunkSize: 5);

        result.Should().BeEmpty();
        counter.ReaderCommandCount.Should().Be(0, "empty input must issue zero round trips");
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_ReturnsFullDuplicateFreeSet_AcrossChunkBoundaries()
    {
        var (ctx, _) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        var ids = new List<TestId>();
        for (var i = 0; i < 10; i++)
        {
            var id = TestId.New();
            ids.Add(id);
            ctx.TestAggregates.Add(new TestAggregate(id, $"Item{i}", new SystemClock()));
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await repo.GetByIdsChunkedAsync(ids, chunkSize: 4);

        result.Should().HaveCount(10);
        result.Select(a => a.Id).Distinct().Should().HaveCount(10);
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_PartialMatch_ReturnsOnlyMatchingEntities()
    {
        var (ctx, _) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        var existingId = TestId.New();
        var missingId = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(existingId, "Existing", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await repo.GetByIdsChunkedAsync([existingId, missingId], chunkSize: 1);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(existingId);
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_ChunkSizeLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var (ctx, _) = CreateContextWithCounter();
        using var disposeCtx = ctx;
        var repo = new ChunkedTestReadRepo(ctx);

        Func<Task> act = () => repo.GetByIdsChunkedAsync([TestId.New()], chunkSize: 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
