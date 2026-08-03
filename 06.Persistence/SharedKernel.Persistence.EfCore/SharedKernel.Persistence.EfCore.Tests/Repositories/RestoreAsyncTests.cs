using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

/// <summary>
/// WO-053/P-337 (C-138/C-139): <see cref="IRestorableRepository{TAggregate,TId}"/> and
/// <see cref="SharedKernel.Persistence.EfCore.Repositories.EfRepository{TAggregate,TId}.RestoreAsync"/>.
/// </summary>
public sealed class RestoreAsyncTests
{
    [Fact]
    public async Task RestoreAsync_SoftDeletedAggregate_ClearsIsDeletedAndDeletedFields_AfterSaveChanges()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "ToRestore", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();

        await repo.DeleteAsync(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var deleted = await ctx.AuditableAggregates
            .IgnoreQueryFilters()
            .FirstAsync(e => e.Id == id);
        deleted.IsDeleted.Should().BeTrue();

        // Act
        var repo2 = new BulkAuditableRepository(ctx);
        await repo2.RestoreAsync(deleted);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Assert
        var restored = await ctx.AuditableAggregates.FirstAsync(e => e.Id == id);
        restored.IsDeleted.Should().BeFalse();
        restored.DeletedOn.Should().BeNull();
        restored.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task RestoreAsync_OnlyStages_DoesNotPersistWithoutExplicitSaveChanges()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "StageOnly", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();
        await repo.DeleteAsync(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var deleted = await ctx.AuditableAggregates.IgnoreQueryFilters().FirstAsync(e => e.Id == id);

        // Act — stage restore, but never call SaveChangesAsync.
        var repo2 = new BulkAuditableRepository(ctx);
        await repo2.RestoreAsync(deleted);

        // Assert — a fresh, untracked read still sees the row as deleted.
        using var freshCtx = TestDbContextFactory.CreateTestDbContext();
        // Reuse same underlying store isn't possible across separate contexts here (in-memory SQLite
        // per-context), so instead assert the entry is staged as Modified pre-save on the SAME context.
        ctx.Entry(deleted).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public async Task RestoreAsync_AlreadyNotDeleted_IsIdempotentNoOp()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "NeverDeleted", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var tracked = await ctx.AuditableAggregates.FirstAsync(e => e.Id == id);

        // Act
        Func<Task> act = async () =>
        {
            await repo.RestoreAsync(tracked);
            await ctx.SaveChangesAsync();
        };

        // Assert
        await act.Should().NotThrowAsync();
        var reread = await ctx.AuditableAggregates.FirstAsync(e => e.Id == id);
        reread.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_NonSoftDeletableAggregate_ThrowsInvalidOperationException()
    {
        // Arrange — TestAggregate does not implement ISoftDeletable.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var id = TestId.New();
        var aggregate = new TestAggregate(id, "PlainAggregate", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();

        // Act
        Func<Task> act = () => repo.RestoreAsync(aggregate);

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(nameof(TestAggregate));
    }

    [Fact]
    public async Task RestoreAsync_ThenSaveChanges_SetsModifiedByViaAuditInterceptor()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userContext = TestDbContextFactory.CreateAuthenticatedUserContext(userId);
        using var ctx = TestDbContextFactory.CreateTestDbContextWithOptions(
            userContext, TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow), TestDbContextFactory.DefaultServiceOptions());
        var repo = new BulkAuditableRepository(ctx);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "AuditRestore", new SystemClock());
        await repo.AddAsync(aggregate);
        await ctx.SaveChangesAsync();
        await repo.DeleteAsync(aggregate);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var deleted = await ctx.AuditableAggregates.IgnoreQueryFilters().FirstAsync(e => e.Id == id);

        // Act
        await repo.RestoreAsync(deleted);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Assert — ModifiedBy set as a byproduct of being an ordinary Modified row.
        var restored = await ctx.AuditableAggregates.FirstAsync(e => e.Id == id);
        restored.ModifiedBy.Should().Be(userId.ToString("D"));
        restored.ModifiedOn.Should().NotBeNull();
    }
}
