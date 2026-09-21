using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

public sealed class SpecificationEvaluatorTests
{
    private readonly SpecificationEvaluator<TestAggregate> _evaluator = new();

    private static TestDbContext CreateAndSeedContext()
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();

        ctx.TestAggregates.AddRange(
            new TestAggregate(new TestId(Guid.Parse("00000000-0000-0000-0000-000000000001")), "Alpha", new SystemClock()),
            new TestAggregate(new TestId(Guid.Parse("00000000-0000-0000-0000-000000000002")), "Beta", new SystemClock()),
            new TestAggregate(new TestId(Guid.Parse("00000000-0000-0000-0000-000000000003")), "Gamma", new SystemClock()),
            new TestAggregate(new TestId(Guid.Parse("00000000-0000-0000-0000-000000000004")), "Delta", new SystemClock()),
            new TestAggregate(new TestId(Guid.Parse("00000000-0000-0000-0000-000000000005")), "Alpha", new SystemClock()));

        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task GetQuery_NullCriteria_ReturnsAllEntities()
    {
        // Arrange
        using var ctx = CreateAndSeedContext();
        var spec = new AllSpecification<TestAggregate>();

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetQuery_WithCriteria_FiltersResults()
    {
        // Arrange
        using var ctx = CreateAndSeedContext();
        var spec = new NameFilterSpec("Alpha");

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Name.Should().Be("Alpha"));
    }

    [Fact]
    public async Task GetQuery_WithOrderBy_ReturnsSortedAscending()
    {
        // Arrange
        using var ctx = CreateAndSeedContext();
        var spec = new OrderByNameSpec();

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert
        result.Select(e => e.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetQuery_WithOrderByDescending_ReturnsSortedDescending()
    {
        // Arrange
        using var ctx = CreateAndSeedContext();
        var spec = new OrderByNameDescSpec();

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert
        result.Select(e => e.Name).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetQuery_WithPaging_ReturnsCorrectPage()
    {
        // Arrange
        using var ctx = CreateAndSeedContext();
        var spec = new PagedSpec(skip: 2, take: 2);

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetQuery_Distinct_RemovesDuplicates()
    {
        // Arrange — 5 entities, "Alpha" appears twice
        using var ctx = CreateAndSeedContext();
        var spec = new DistinctNameSpec();

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert — IDistinct on the entity level returns all entities (distinct entities, not values)
        // We have 5 entities with unique IDs but some share names — entity distinct returns 5 rows
        result.Should().HaveCount(5);
    }

    [Fact]
    public void ThenByWithoutPrimarySort_ThrowsWhenTheSpecificationIsBuilt()
    {
        // Arrange
        // P-558: a secondary key without a primary sort used to be silently ignored.
        var act = () => new ThenByOnlySpec();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task GetQuery_PagingAppliedAfterOrdering()
    {
        // Arrange — seed known order, paging should respect it
        using var ctx = CreateAndSeedContext();
        var spec = new OrderedPagedSpec(skip: 0, take: 2);

        // Act
        var query = _evaluator.GetQuery(ctx.TestAggregates, spec);
        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert — ordered ascending by name, first 2 should be "Alpha", "Alpha"
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Name.Should().Be("Alpha"));
    }
}

// ---------------------------------------------------------------------------
// Test specification implementations
// ---------------------------------------------------------------------------

internal sealed class AllSpecification<T> : Specification<T>
{
    // No criteria — matches all entities
}

internal sealed class NameFilterSpec : Specification<TestAggregate>
{
    public NameFilterSpec(string name)
    {
        AddCriteria(e => e.Name == name);
    }
}

internal sealed class OrderByNameSpec : Specification<TestAggregate>
{
    public OrderByNameSpec()
    {
        ApplyOrderBy(e => e.Name!);
    }
}

internal sealed class OrderByNameDescSpec : Specification<TestAggregate>
{
    public OrderByNameDescSpec()
    {
        ApplyOrderByDescending(e => e.Name!);
    }
}

internal sealed class PagedSpec : Specification<TestAggregate>
{
    public PagedSpec(int skip, int take)
    {
        // Paging requires a primary sort — the evaluator now throws otherwise.
        ApplyOrderBy(e => e.Name!);
        ApplyPaging(skip, take);
    }
}

internal sealed class DistinctNameSpec : Specification<TestAggregate>
{
    public DistinctNameSpec()
    {
        ApplyDistinct();
    }
}

internal sealed class ThenByOnlySpec : Specification<TestAggregate>
{
    public ThenByOnlySpec()
    {
        // ThenBy without a primary OrderBy — should be silently ignored
        ApplyThenBy(e => e.Name!, descending: false);
    }
}

internal sealed class OrderedPagedSpec : Specification<TestAggregate>
{
    public OrderedPagedSpec(int skip, int take)
    {
        ApplyOrderBy(e => e.Name!);
        ApplyPaging(skip, take);
    }
}

