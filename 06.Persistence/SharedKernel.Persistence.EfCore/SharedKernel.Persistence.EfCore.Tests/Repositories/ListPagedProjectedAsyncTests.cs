using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using System.Linq.Expressions;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// Specs for ListPagedProjectedAsync tests
// ---------------------------------------------------------------------------

/// <summary>Paged projection spec that orders by Name and projects to string (Name).</summary>
internal sealed class PagedNameProjectionSpec : PagedSpecification<TestAggregate>,
    IProjectionSpecification<TestAggregate, string>
{
    public Expression<Func<TestAggregate, string>> Selector { get; } = e => e.Name;

    public PagedNameProjectionSpec(int page, int pageSize) : base(page, pageSize)
    {
        ApplyOrderBy(e => e.Name!);
    }
}

// ---------------------------------------------------------------------------
// T-29 — ListPagedProjectedAsync integration tests
// ---------------------------------------------------------------------------

public sealed class ListPagedProjectedAsyncTests
{
    private static EfReadRepository<TestAggregate, TestId> CreateReadRepo(TestDbContext ctx)
        => new EfReadRepositoryImpl(ctx);

    /// <summary>Concrete read repo for these tests.</summary>
    private sealed class EfReadRepositoryImpl(TestDbContext ctx)
        : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>());

    // -----------------------------------------------------------------------
    // Test 1: 10 aggregates, page 2 size 3 → 3 items, TotalCount == 10
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListPagedProjectedAsync_Page2_Size3_Of10_Returns3Items_TotalCount10()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 10; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await readRepo.ListPagedProjectedAsync(
            new PagedNameProjectionSpec(page: 2, pageSize: 3));

        result.Items.Should().HaveCount(3, "page 2 of 3 from 10 items has 3 entries");
        result.TotalCount.Should().Be(10, "10 items were saved");
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(3);
        result.Items.Should().AllBeOfType<string>("projection is to Name string");
    }

    // -----------------------------------------------------------------------
    // Test 2: Empty set → TotalCount == 0, Items == []
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListPagedProjectedAsync_EmptySet_Returns_ZeroTotalCount_EmptyItems()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        var result = await readRepo.ListPagedProjectedAsync(
            new PagedNameProjectionSpec(page: 1, pageSize: 10));

        result.TotalCount.Should().Be(0, "no items in the database");
        result.Items.Should().BeEmpty("no items match");
    }

    // -----------------------------------------------------------------------
    // Test 3: Page beyond data → empty items, correct TotalCount
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListPagedProjectedAsync_PageBeyondData_Returns_EmptyItems_CorrectTotalCount()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        for (var i = 1; i <= 5; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await readRepo.ListPagedProjectedAsync(
            new PagedNameProjectionSpec(page: 10, pageSize: 5)); // page 10 of 5 items

        result.Items.Should().BeEmpty("page 10 is beyond available data (only 5 items total)");
        result.TotalCount.Should().Be(5, "total count reflects all items, not just the current page");
    }

    // -----------------------------------------------------------------------
    // Test 4: Projection applied at DB level — result is string, not aggregate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListPagedProjectedAsync_ProjectsAtDbLevel_ReturnsOnlyMappedFields()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = CreateReadRepo(ctx);

        var uniqueName = $"UniqueProjected_{Guid.NewGuid():N}";
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), uniqueName, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await readRepo.ListPagedProjectedAsync(
            new PagedNameProjectionSpec(page: 1, pageSize: 10));

        // Each item is a string (projected Name), not a TestAggregate.
        result.Items.Should().AllSatisfy(item =>
            item.Should().BeOfType<string>("selector projects to string"));
        result.Items.Should().Contain(uniqueName);
    }

    // -----------------------------------------------------------------------
    // Test 5: Contract shape — IReadRepository declares ListPagedProjectedAsync
    // -----------------------------------------------------------------------

    [Fact]
    public void IReadRepository_Declares_ListPagedProjectedAsync()
    {
        var method = typeof(SharedKernel.Persistence.Abstractions.Repositories.IReadRepository<,>)
            .GetMethod("ListPagedProjectedAsync");
        method.Should().NotBeNull(
            "IReadRepository<TAggregate, TId> must declare ListPagedProjectedAsync<TResult>");
    }
}
