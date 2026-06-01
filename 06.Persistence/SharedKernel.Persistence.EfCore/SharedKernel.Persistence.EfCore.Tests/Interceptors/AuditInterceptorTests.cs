using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

public sealed class AuditInterceptorTests
{
    [Fact]
    public async Task SaveChanges_OnAddedEntity_SetsCreatedByAndCreatedOn()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = "alice";
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: TestDbContextFactory.CreateUserContext(userId),
            clock: TestDbContextFactory.CreateClock(now));

        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());

        // Act
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Assert — reload to confirm persisted values
        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be(userId);
        saved.CreatedOn.Should().Be(now);
    }

    [Fact]
    public async Task SaveChanges_OnModifiedEntity_SetsModifiedByAndModifiedOn()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var modifiedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = "bob";

        var clockMock = Substitute.For<IClock>();
        clockMock.UtcNow.Returns(createdAt);

        var userMock = TestDbContextFactory.CreateUserContext(userId);

        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: userMock,
            clock: clockMock);

        var aggregate = new AuditableTestAggregate(TestId.New(), "Original", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        // Now update the clock mock to return a later time
        clockMock.UtcNow.Returns(modifiedAt);

        // Reload and modify
        ctx.ChangeTracker.Clear();
        var toModify = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        ctx.Entry(toModify!).CurrentValues["Name"] = "Modified";
        await ctx.SaveChangesAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var result = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        result!.ModifiedBy.Should().Be(userId);
        result.ModifiedOn.Should().Be(modifiedAt);
    }

    [Fact]
    public async Task SaveChanges_EmptyUserId_FallsBackToSystem()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: TestDbContextFactory.CreateUserContext(string.Empty));

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
