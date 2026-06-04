using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

/// <summary>
/// T-32: GetByIdsAsync expression-tree IN clause with strongly-typed IDs (P-105 Fix 2).
/// </summary>
public sealed class GetByIdsAsyncStronglyTypedIdTests
{
    private sealed class TestReadRepo : EfReadRepository<TestAggregate, TestId>
    {
        public TestReadRepo(TestDbContext ctx)
            : base(ctx, new SpecificationEvaluator<TestAggregate>()) { }
    }

    [Fact]
    public async Task GetByIdsAsync_WithStronglyTypedId_Returns_MatchingEntities()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id1 = TestId.New();
        var id2 = TestId.New();
        var id3 = TestId.New();

        ctx.TestAggregates.AddRange(
            new TestAggregate(id1, "One", new SystemClock()),
            new TestAggregate(id2, "Two", new SystemClock()),
            new TestAggregate(id3, "Three", new SystemClock()));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new TestReadRepo(ctx);

        // Act
        var result = await repo.GetByIdsAsync([id1, id3]);

        // Assert
        result.Should().HaveCount(2);
        result.Select(e => e.Id).Should().BeEquivalentTo(new[] { id1, id3 });
    }

    [Fact]
    public async Task GetByIdsAsync_PartialMatch_Returns_OnlyMatchingEntities()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var id1 = TestId.New();
        var missingId = TestId.New();

        ctx.TestAggregates.Add(new TestAggregate(id1, "Existing", new SystemClock()));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new TestReadRepo(ctx);

        // Act
        var result = await repo.GetByIdsAsync([id1, missingId]);

        // Assert — only id1 found, missingId produces no entry
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(id1);
    }

    [Fact]
    public async Task GetByIdsAsync_EmptyInput_Returns_EmptyList()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "X", new SystemClock()));
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new TestReadRepo(ctx);

        // Act
        var result = await repo.GetByIdsAsync([]);

        // Assert
        result.Should().BeEmpty();
    }
}
