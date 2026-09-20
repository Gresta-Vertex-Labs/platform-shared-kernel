using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

public sealed class SoftDeleteInterceptorTests
{
    [Fact]
    public async Task SaveChanges_DeletedSoftDeletableEntity_ChangesStateToModified()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: TestDbContextFactory.CreateAuthenticatedActorContext(userId),
            clock: TestDbContextFactory.CreateClock(now));

        var aggregate = new AuditableTestAggregate(TestId.New(), "ToSoftDelete", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        // Assert — entity should still exist in DB with IsDeleted = true
        ctx.ChangeTracker.Clear();
        var stillThere = await ctx.AuditableAggregates
            .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == aggregate.Id);

        stillThere.Should().NotBeNull();
        stillThere!.IsDeleted.Should().BeTrue();
        stillThere.DeletedOn.Should().Be(now);
        stillThere.DeletedBy.Should().Be(userId.ToString("D"));
    }

    [Fact]
    public async Task SaveChanges_Unauthenticated_DeletedBy_IsSystem()
    {
        // Arrange — an actor context resolving to "system" writes it verbatim as DeletedBy
        var now = DateTimeOffset.UtcNow;
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: new FakeAuditActorContext("system"),
            clock: TestDbContextFactory.CreateClock(now));

        var aggregate = new AuditableTestAggregate(TestId.New(), "SoftDeleteSystem", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var stillThere = await ctx.AuditableAggregates
            .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == aggregate.Id);
        stillThere!.DeletedBy.Should().Be("system");
    }

    [Fact]
    public async Task SaveChanges_AuthenticatedWithGuidEmpty_DeletedBy_IsSystem()
    {
        // Arrange — SoftDeleteInterceptor writes IAuditActorContext.ActorId verbatim,
        // whatever the registered actor context resolves it to (here, "system").
        using var ctx = TestDbContextFactory.CreateTestDbContext(actorContext: new FakeAuditActorContext("system"));
        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var stillThere = await ctx.AuditableAggregates
            .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == aggregate.Id);
        stillThere!.DeletedBy.Should().Be("system");
    }

    [Fact]
    public async Task SaveChanges_SoftDeletedEntity_IsExcludedByGlobalQueryFilter()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var aggregate = new AuditableTestAggregate(TestId.New(), "Hidden", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        // Assert — normal query does not return soft-deleted entity
        ctx.ChangeTracker.Clear();
        var notFound = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        notFound.Should().BeNull();

        var listResult = ctx.AuditableAggregates.ToList();
        listResult.Should().NotContain(e => e.Id == aggregate.Id);
    }

    [Fact]
    public async Task SaveChanges_DeletedNonSoftDeletableEntity_PhysicallyRemovesRow()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var aggregate = new HardDeleteAggregate(TestId.New(), "HardDelete", new SystemClock());
        ctx.HardDeleteAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.HardDeleteAggregates.FindAsync(aggregate.Id);
        ctx.HardDeleteAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        // Assert — entity is gone entirely
        ctx.ChangeTracker.Clear();
        var gone = await ctx.HardDeleteAggregates.FindAsync(aggregate.Id);
        gone.Should().BeNull();

        // Confirm it's not in DB even with IgnoreQueryFilters (no filter applies here anyway)
        var count = ctx.HardDeleteAggregates.Count();
        count.Should().Be(0);
    }
}
