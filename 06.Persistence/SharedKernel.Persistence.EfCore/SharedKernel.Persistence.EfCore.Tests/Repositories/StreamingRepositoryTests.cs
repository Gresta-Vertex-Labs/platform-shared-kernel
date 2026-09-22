using System.Linq.Expressions;
using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// Specs for StreamAsync / StreamProjectedAsync tests (C-87, C-88)
// ---------------------------------------------------------------------------

/// <summary>Tracked spec — explicitly sets <c>AsNoTracking = false</c> via not calling ApplyNoTracking.</summary>
internal sealed class TrackedAllSpec : Specification<TestAggregate>
{
    public TrackedAllSpec() => ApplyOrderBy(e => e.Name!);
}

/// <summary>Spec with Skip/Take applied, ordered by Name.</summary>
internal sealed class PagedOrderedSpec : Specification<TestAggregate>
{
    public PagedOrderedSpec(int skip, int take)
    {
        ApplyOrderBy(e => e.Name!);
        ApplyPaging(skip, take);
    }
}

/// <summary>Projection spec — projects to Name string, ordered.</summary>
internal sealed class StreamNameProjectionSpec : Specification<TestAggregate>,
    IProjectionSpecification<TestAggregate, string>
{
    public Expression<Func<TestAggregate, string>> Selector { get; } = e => e.Name;

    public StreamNameProjectionSpec() => ApplyOrderBy(e => e.Name!);
}

// ---------------------------------------------------------------------------
// EfReadRepository.StreamAsync / StreamProjectedAsync tests (C-87, C-88)
// ---------------------------------------------------------------------------

public sealed class StreamingRepositoryTests
{
    private static EfReadRepository<TestAggregate, TestId> CreateReadRepo(TestDbContext ctx)
        => new EfReadRepositoryImpl(ctx);

    private sealed class EfReadRepositoryImpl(TestDbContext ctx)
        : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>());

    [Fact]
    public async Task StreamAsync_NRows_YieldsNItems()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 5; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var results = new List<TestAggregate>();
        await foreach (var item in readRepo.StreamAsync(new TrackedAllSpec()))
            results.Add(item);

        results.Should().HaveCount(5);
        results.Select(r => r.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task StreamAsync_ForcesNoTracking_RegardlessOfSpecFlag()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 3; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // TrackedAllSpec does NOT call ApplyNoTracking — AsNoTracking == false on the spec.
        var spec = new TrackedAllSpec();

        var count = 0;
        await foreach (var _ in readRepo.StreamAsync(spec))
            count++;

        count.Should().Be(3);
        ctx.ChangeTracker.Entries().Should().BeEmpty(
            "StreamAsync must force AsNoTracking() regardless of the spec's AsNoTracking flag");
    }

    [Fact]
    public async Task StreamAsync_WithSkipTake_AppliesRowWindow()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 10; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var results = new List<TestAggregate>();
        await foreach (var item in readRepo.StreamAsync(new PagedOrderedSpec(skip: 2, take: 3)))
            results.Add(item);

        results.Should().HaveCount(3);
        results.Select(r => r.Name).Should().Equal("Item03", "Item04", "Item05");
    }

    [Fact]
    public async Task StreamAsync_EmptyResult_YieldsNoItems()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        var count = 0;
        await foreach (var _ in readRepo.StreamAsync(new TrackedAllSpec()))
            count++;

        count.Should().Be(0);
    }

    [Fact]
    public async Task StreamAsync_CancellationMidEnumeration_ThrowsOperationCanceledException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 5; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            var count = 0;
            await foreach (var _ in readRepo.StreamAsync(new TrackedAllSpec(), cts.Token))
            {
                count++;
                if (count == 1)
                    await cts.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StreamProjectedAsync_NRows_YieldsNItems_MatchesListProjectedAsync()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 5; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new StreamNameProjectionSpec();

        var streamed = new List<string>();
        await foreach (var item in readRepo.StreamProjectedAsync(spec))
            streamed.Add(item);

        var listed = await readRepo.ListProjectedAsync(spec);

        streamed.Should().HaveCount(5);
        streamed.Should().Equal(listed);
    }

    [Fact]
    public async Task StreamProjectedAsync_ForcesNoTracking()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 3; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var count = 0;
        await foreach (var _ in readRepo.StreamProjectedAsync(new StreamNameProjectionSpec()))
            count++;

        count.Should().Be(3);
        ctx.ChangeTracker.Entries().Should().BeEmpty(
            "StreamProjectedAsync must force AsNoTracking() on the underlying query");
    }

    [Fact]
    public void IReadRepository_Declares_StreamAsync_And_StreamProjectedAsync()
    {
        var readRepoType = typeof(IReadRepository<,>);

        var streamMethod = readRepoType.GetMethod("StreamAsync");
        streamMethod.Should().NotBeNull("IReadRepository<TAggregate, TId> must declare StreamAsync (C-87)");

        var streamProjectedMethod = readRepoType.GetMethod("StreamProjectedAsync");
        streamProjectedMethod.Should().NotBeNull(
            "IReadRepository<TAggregate, TId> must declare StreamProjectedAsync<TResult> (C-87)");
    }
}
