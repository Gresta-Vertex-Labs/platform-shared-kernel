using FluentAssertions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// EfRepository.ExistsAsync expression-tree predicate
// ---------------------------------------------------------------------------

public sealed class ExistsAsyncExpressionTreeTests
{
    [Fact]
    public async Task ExistsAsync_Returns_True_ForExistingId()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        var id = TestId.New();
        await repo.AddAsync(new TestAggregate(id, "ExistCheck", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var exists = await repo.ExistsAsync(id);

        exists.Should().BeTrue("the entity with that ID was saved to the database");
    }

    [Fact]
    public async Task ExistsAsync_Returns_False_ForMissingId()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        var missingId = TestId.New(); // never persisted

        var exists = await repo.ExistsAsync(missingId);

        exists.Should().BeFalse("no entity with that ID exists in the database");
    }

    [Fact]
    public async Task ExistsAsync_DoesNot_Materialize_Entity()
    {
        // After ExistsAsync, the ChangeTracker must be empty — no tracked entity returned.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        var id = TestId.New();
        await repo.AddAsync(new TestAggregate(id, "NoMaterialize", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        _ = await repo.ExistsAsync(id);

        ctx.ChangeTracker.Entries().Should().BeEmpty(
            "ExistsAsync must issue an EXISTS/ANY check, not materialize the entity");
    }

    [Fact]
    public async Task ExistsAsync_WorksCorrectly_WithMultipleEntities()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new TestAggregateRepository(ctx);

        var id1 = TestId.New();
        var id2 = TestId.New();
        var missingId = TestId.New();

        await repo.AddAsync(new TestAggregate(id1, "A", new SystemClock()));
        await repo.AddAsync(new TestAggregate(id2, "B", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        (await repo.ExistsAsync(id1)).Should().BeTrue();
        (await repo.ExistsAsync(id2)).Should().BeTrue();
        (await repo.ExistsAsync(missingId)).Should().BeFalse();
    }
}
