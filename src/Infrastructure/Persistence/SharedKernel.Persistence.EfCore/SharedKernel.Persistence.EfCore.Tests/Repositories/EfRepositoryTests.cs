using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// Concrete repository implementations for tests
// ---------------------------------------------------------------------------

internal sealed class TestAggregateRepository(TestDbContext ctx)
    : EfRepository<TestAggregate, TestId>(ctx);

internal sealed class TestAggregateReadRepository(TestDbContext ctx)
    : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>());

// ---------------------------------------------------------------------------
// Write-side tests
// ---------------------------------------------------------------------------

public sealed class EfRepositoryTests
{
    [Fact]
    public async Task AddAsync_And_GetByIdAsync_RoundTrip()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);
        var id = TestId.New();
        var aggregate = new TestAggregate(id, "Hello", new SystemClock());

        // Act
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var found = await repo.GetByIdAsync(id);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(id);
        found.Name.Should().Be("Hello");
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);
        var id = TestId.New();
        var aggregate = new TestAggregate(id, "Original", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        ctx.ChangeTracker.Clear();
        var loaded = await repo.GetByIdAsync(id);
        ctx.Entry(loaded!).CurrentValues["Name"] = "Updated";
        await repo.UpdateAsync(loaded!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await repo.GetByIdAsync(id);

        // Assert
        reloaded!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);
        var id = TestId.New();
        var aggregate = new TestAggregate(id, "ToDelete", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        ctx.ChangeTracker.Clear();
        var loaded = await repo.GetByIdAsync(id);
        await repo.DeleteAsync(loaded!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var notFound = await repo.GetByIdAsync(id);

        // Assert
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        // Act
        var result = await repo.GetByIdAsync(TestId.New());

        // Assert
        result.Should().BeNull();
    }
}

// ---------------------------------------------------------------------------
// Read-side tests
// ---------------------------------------------------------------------------

public sealed class EfReadRepositoryTests
{
    [Fact]
    public async Task GetBySpecAsync_WithMatchingCriteria_ReturnsFirstMatch()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        var id = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id, "SpecTarget", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameSpec("SpecTarget");

        // Act
        var result = await readRepo.FirstOrDefaultAsync(spec);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("SpecTarget");
    }

    [Fact]
    public async Task ListAsync_WithSpec_ReturnsMatchingEntities()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "Group1", new SystemClock()),
            new TestAggregate(TestId.New(), "Group1", new SystemClock()),
            new TestAggregate(TestId.New(), "Group2", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameSpec("Group1");

        // Act
        var result = await readRepo.ListAsync(spec);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Name.Should().Be("Group1"));
    }

    [Fact]
    public async Task CountAsync_WithSpec_ReturnsCorrectCount()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "Count1", new SystemClock()),
            new TestAggregate(TestId.New(), "Count1", new SystemClock()),
            new TestAggregate(TestId.New(), "Other", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameSpec("Count1");

        // Act
        var count = await readRepo.CountAsync(spec);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task AnyAsync_WithMatchingSpec_ReturnsTrue()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Exists", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new TestNameSpec("Exists");

        // Act
        var any = await readRepo.AnyAsync(spec);

        // Assert
        any.Should().BeTrue();
    }

    [Fact]
    public async Task AnyAsync_WithNonMatchingSpec_ReturnsFalse()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        ctx.ChangeTracker.Clear();

        var spec = new TestNameSpec("DoesNotExist");

        // Act
        var any = await readRepo.AnyAsync(spec);

        // Assert
        any.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_WithPagedSpec_ReturnsPaginatedResults()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new TestAggregateReadRepository(ctx);
        for (var i = 0; i < 10; i++)
            ctx.TestAggregates.Add(new TestAggregate(TestId.New(), $"Item{i:D2}", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new PagedNoFilterSpec(skip: 3, take: 4);

        // Act
        var result = await readRepo.ListAsync(spec);

        // Assert
        result.Should().HaveCount(4);
    }
}

// ---------------------------------------------------------------------------
// Local test spec helpers
// ---------------------------------------------------------------------------

internal sealed class TestNameSpec : Specification<TestAggregate>
{
    public TestNameSpec(string name)
    {
        AddCriteria(e => e.Name == name);
    }
}

internal sealed class PagedNoFilterSpec : Specification<TestAggregate>
{
    public PagedNoFilterSpec(int skip, int take)
    {
        ApplyOrderBy(e => e.Name!);
        ApplyPaging(skip, take);
    }
}
