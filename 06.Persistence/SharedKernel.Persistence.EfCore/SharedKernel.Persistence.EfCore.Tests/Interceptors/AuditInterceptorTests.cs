using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

public sealed class AuditInterceptorTests
{
    [Fact]
    public async Task SaveChanges_OnAddedEntity_SetsCreatedByAndCreatedOn()
    {
        // Arrange
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: TestDbContextFactory.CreateAuthenticatedUserContext(userId),
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
        // Arrange — P-091: IsAuthenticated == true && UserId != Guid.Empty → userId.ToString("D")
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: TestDbContextFactory.CreateAuthenticatedUserContext(userId));

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
        // Arrange — P-091: IsAuthenticated == false → "system"
        using var ctx = TestDbContextFactory.CreateTestDbContext(
            userContext: TestDbContextFactory.CreateUnauthenticatedUserContext());

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
    public async Task SaveChanges_AuthenticatedWithoutSubject_WritesSystem()
    {
        // Arrange — P-091: IsAuthenticated == true but no SubjectId → "system"
        var mock = Substitute.For<IUserContext>();
        mock.SubjectId.Returns((string?)null);
        mock.IsAuthenticated.Returns(true);
        mock.Roles.Returns([]);

        using var ctx = TestDbContextFactory.CreateTestDbContext(userContext: mock);
        var aggregate = new AuditableTestAggregate(TestId.New(), "Test", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.FindAsync(aggregate.Id);
        saved!.CreatedBy.Should().Be("system");
    }

    [Fact]
    public async Task SaveChanges_OnModifiedEntity_SetsModifiedByAndModifiedOn()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var modifiedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var userId = Guid.NewGuid();

        var clockMock = Substitute.For<IClock>();
        clockMock.UtcNow.Returns(createdAt);

        var userMock = TestDbContextFactory.CreateAuthenticatedUserContext(userId);

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
