using FluentAssertions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

public sealed class SaveChangesAuditStampingTests
{
    [Fact]
    public async Task SaveChanges_OnAddedEntity_SetsCreatedByAndCreatedOn()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: TestDbContextFactory.CreateAuthenticatedActorContext(userId),
            clock: TestDbContextFactory.CreateClock(now));

        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());

        // Act
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Assert — reload to confirm persisted values
        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be(userId.ToString("D"));
        saved.CreatedOn.Should().Be(now);
    }

    [Fact]
    public async Task SaveChanges_Authenticated_WithValidUserId_WritesGuidDFormat()
    {
        // Arrange — AuditInterceptor writes IAuditActorContext.ActorId verbatim.
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: TestDbContextFactory.CreateAuthenticatedActorContext(userId));

        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public async Task SaveChanges_Unauthenticated_WritesSystem()
    {
        // Arrange — the unauthenticated fallback string now comes entirely from whichever
        // IAuditActorContext is registered (the default AnonymousActorContext, tested separately);
        // here a fake context returning "system" proves the interceptor writes ActorId verbatim.
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: new FakeAuditActorContext("system"));

        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());

        // Act
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be("system");
    }

    [Fact]
    public async Task SaveChanges_ActorContextReturnsArbitraryString_WritesItVerbatim()
    {
        // Arrange — AuditInterceptor no longer inspects IsAuthenticated/SubjectId itself;
        // it copies IAuditActorContext.ActorId as-is, whatever that value is.
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: new FakeAuditActorContext("service-account"));
        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be("service-account");
    }

    [Fact]
    public async Task SaveChanges_OnModifiedEntity_SetsModifiedByAndModifiedOn()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var modifiedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();

        var clock = new FakeClock(createdAt);
        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(userId);

        using var ctx = TestDbContextFactory.CreateTestDbContext(
            actorContext: actorContext,
            clock: clock);

        var aggregate = new AuditableTestAggregate(TestId.New(), "Original", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Now update the clock to return a later time
        clock.UtcNow = modifiedAt;

        // Reload and modify
        ctx.ChangeTracker.Clear();
        var toModify = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.Entry(toModify!).CurrentValues["Name"] = "Modified";
        await ctx.SaveChangesAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var result = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        result!.ModifiedBy.Should().Be(userId.ToString("D"));
        result.ModifiedOn.Should().Be(modifiedAt);
    }

    [Fact]
    public async Task SaveChanges_DeletedNonSoftDeletableEntity_DoesNotMutateAuditFields()
    {
        // Arrange — HardDeleteAggregate does not implement IHasAudit or ISoftDeletable
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var aggregate = new HardDeleteAggregate(TestId.New(), "ToDelete", new SystemClock());
        ctx.HardDeleteAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Act — should physically delete without interceptor mutation
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.HardDeleteAggregates.FindAsync(aggregate.Id);
        ctx.HardDeleteAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();

        // Assert — entity is gone (no soft-delete, no exception)
        ctx.ChangeTracker.Clear();
        var gone = await ctx.HardDeleteAggregates.FindAsync(aggregate.Id);
        gone.Should().BeNull();
    }
}
