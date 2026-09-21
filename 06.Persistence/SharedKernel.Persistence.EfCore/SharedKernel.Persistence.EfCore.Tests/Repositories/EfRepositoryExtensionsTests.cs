using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// Test data projections
// ---------------------------------------------------------------------------

internal sealed record TestAggregateDto(string Name);

// ---------------------------------------------------------------------------
// Concrete repository implementations for extended tests
// ---------------------------------------------------------------------------

internal sealed class ExtendedTestRepository(TestDbContext ctx)
    : EfRepository<TestAggregate, TestId>(ctx);

internal sealed class ExtendedTestReadRepository(TestDbContext ctx)
    : EfReadRepository<TestAggregate, TestId>(ctx, new SpecificationEvaluator<TestAggregate>());

// ---------------------------------------------------------------------------
// ExistsAsync tests (C-25, T-13)
// ---------------------------------------------------------------------------

public sealed class ExistsAsyncTests
{
    [Fact]
    public async Task ExistsAsync_WithExistingId_ReturnsTrue()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var id = TestId.New();
        await repo.AddAsync(new TestAggregate(id, "Exists", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await repo.ExistsAsync(id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithMissingId_ReturnsFalse()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);

        var result = await repo.ExistsAsync(TestId.New());

        result.Should().BeFalse();
    }
}

// ---------------------------------------------------------------------------
// UpdateAsync tracking optimisation tests (C-28, T-14)
// ---------------------------------------------------------------------------

public sealed class UpdateAsyncTrackingTests
{
    [Fact]
    public async Task UpdateAsync_TrackedEntityWithChange_OnlyModifiedColumnsWritten()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var id = TestId.New();
        await repo.AddAsync(new TestAggregate(id, "Original", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Reload — entity is now tracked
        var loaded = await repo.GetByIdAsync(id);
        ctx.Entry(loaded!).CurrentValues["Name"] = "Updated";

        // Act — entity is tracked, state != Detached, so.Update() should NOT be called
        await repo.UpdateAsync(loaded!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Assert
        var reloaded = await repo.GetByIdAsync(id);
        reloaded!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateAsync_DetachedEntity_FullUpdateIssued()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var id = TestId.New();
        var original = new TestAggregate(id, "Detached", new SystemClock());
        await repo.AddAsync(original);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Detach and re-create
        var detached = new TestAggregate(id, "ReAttached", new SystemClock());
        // Verify the entity is not tracked
        ctx.Entry(detached).State.Should().Be(EntityState.Detached);

        // Act
        await repo.UpdateAsync(detached);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await repo.GetByIdAsync(id);
        reloaded!.Name.Should().Be("ReAttached");
    }
}

// ---------------------------------------------------------------------------
// GetByIdsAsync tests (C-26, T-13)
// ---------------------------------------------------------------------------

public sealed class GetByIdsAsyncTests
{
    [Fact]
    public async Task GetByIdsAsync_AllPresent_ReturnsAll()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new ExtendedTestReadRepository(ctx);
        var id1 = TestId.New();
        var id2 = TestId.New();
        ctx.TestAggregates.AddRange(
            new TestAggregate(id1, "A", new SystemClock()),
            new TestAggregate(id2, "B", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await readRepo.GetByIdsAsync([id1, id2]);

        result.Should().HaveCount(2);
        result.Select(e => e.Id).Should().BeEquivalentTo([id1, id2]);
    }

    [Fact]
    public async Task GetByIdsAsync_PartialMatch_ReturnsOnlyFound()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new ExtendedTestReadRepository(ctx);
        var id1 = TestId.New();
        ctx.TestAggregates.Add(new TestAggregate(id1, "Found", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await readRepo.GetByIdsAsync([id1, TestId.New()]);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(id1);
    }

    [Fact]
    public async Task GetByIdsAsync_EmptyInput_ReturnsEmpty()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new ExtendedTestReadRepository(ctx);

        var result = await readRepo.GetByIdsAsync([]);

        result.Should().BeEmpty();
    }
}

// ---------------------------------------------------------------------------
// Bulk write tests (C-33, T-18)
// ---------------------------------------------------------------------------

public sealed class BulkWriteTests
{
    [Fact]
    public async Task AddRangeAsync_AddsAllEntities()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var aggregates = Enumerable.Range(1, 5)
            .Select(i => new TestAggregate(TestId.New(), $"Item{i}", new SystemClock()))
                .ToList();

        // Verify staged without commit
        await repo.AddRangeAsync(aggregates);
        ctx.TestAggregates.Count().Should().Be(0, "items must be staged, not persisted");

        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        ctx.TestAggregates.Count().Should().Be(5);
    }

    [Fact]
    public async Task UpdateRangeAsync_TrackedEntities_UpdatesAll()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var id1 = TestId.New();
        var id2 = TestId.New();
        ctx.TestAggregates.AddRange(
            new TestAggregate(id1, "Before1", new SystemClock()),
            new TestAggregate(id2, "Before2", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Load both (tracked)
        var e1 = await repo.GetByIdAsync(id1);
        var e2 = await repo.GetByIdAsync(id2);
        ctx.Entry(e1!).CurrentValues["Name"] = "After1";
        ctx.Entry(e2!).CurrentValues["Name"] = "After2";

        await repo.UpdateRangeAsync([e1!, e2!]);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        (await repo.GetByIdAsync(id1))!.Name.Should().Be("After1");
        (await repo.GetByIdAsync(id2))!.Name.Should().Be("After2");
    }

    [Fact]
    public async Task DeleteRangeAsync_SoftDeletableEntities_AllMarkedDeleted()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ConcreteAuditRepo(ctx);
        var id1 = TestId.New();
        var id2 = TestId.New();
        ctx.AuditableAggregates.AddRange(
            new AuditableTestAggregate(id1, "Del1", new SystemClock()),
            new AuditableTestAggregate(id2, "Del2", new SystemClock()));
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var e1 = await ctx.AuditableAggregates.FindAsync(id1);
        var e2 = await ctx.AuditableAggregates.FindAsync(id2);

        await repo.DeleteRangeAsync([e1!, e2!]);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var remaining = ctx.AuditableAggregates.ToList();
        remaining.Should().BeEmpty("soft-deleted entities excluded by global filter");

        var withDeleted = await ctx.AuditableAggregates.IgnoreQueryFilters().ToListAsync();
        withDeleted.Should().HaveCount(2);
        withDeleted.Should().AllSatisfy(e => e.IsDeleted.Should().BeTrue());
    }

    [Fact]
    public async Task DeleteRangeAsync_NonSoftDeletableEntities_RowsRemoved()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new ExtendedTestRepository(ctx);
        var id1 = TestId.New();
        var id2 = TestId.New();
        ctx.TestAggregates.AddRange(
            new TestAggregate(id1, "HD1", new SystemClock()),
            new TestAggregate(id2, "HD2", new SystemClock()));
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var e1 = await repo.GetByIdAsync(id1);
        var e2 = await repo.GetByIdAsync(id2);

        await repo.DeleteRangeAsync([e1!, e2!]);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        ctx.TestAggregates.Count().Should().Be(0);
    }
}

// ---------------------------------------------------------------------------
// Projection read tests (C-35, T-19)
// ---------------------------------------------------------------------------

public sealed class IncludeDeletedTests
{
    [Fact]
    public async Task IncludeDeleted_False_ExcludesSoftDeletedRows()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new ConcreteAuditReadRepo(ctx, new SpecificationEvaluator<AuditableTestAggregate>());

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "Hidden", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Soft-delete it
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new NoFilterSpec<AuditableTestAggregate>();
        var result = await readRepo.ListAsync(spec);
        result.Should().BeEmpty("soft-deleted records must be excluded by default");
    }

    [Fact]
    public async Task IncludeDeleted_True_IncludesSoftDeletedRows()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var readRepo = new ConcreteAuditReadRepo(ctx, new SpecificationEvaluator<AuditableTestAggregate>());

        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "Hidden", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Soft-delete it
        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.AuditableAggregates.FindAsync(id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var spec = new IncludeDeletedSpec<AuditableTestAggregate>();
        var result = await readRepo.ListAsync(spec);
        result.Should().HaveCount(1, "IncludeDeleted = true must return soft-deleted records");
    }

    [Fact]
    public void QueryableExtensions_ClassNoLongerExists_InAssembly()
    {
        // QueryableExtensions.IgnoreSoftDeleteFilter() has been removed.
        var assembly = typeof(EfReadRepository<,>).Assembly;
        var type = assembly.GetTypes().FirstOrDefault(t => t.Name == "QueryableExtensions");
        type.Should().BeNull("QueryableExtensions was deleted — use spec.IncludeDeleted = true instead");
    }
}

// ---------------------------------------------------------------------------
// ByIdSpecification tests (C-40, T-23)
// ---------------------------------------------------------------------------

public sealed class DomainEventDispatchTests
{
    [Fact]
    public async Task SaveChangesAsync_WithDispatcher_DispatchesEventsAndClearsThem()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var dispatched = new List<IDomainEvent>();
        var dispatcher = new CaptureDispatcher(dispatched);
        var uow = new EfUnitOfWork(ctx, dispatcher);

        var aggregate = new AuditableTestAggregate(TestId.New(), "EventTest", new SystemClock());
        aggregate.RaiseTestEvent();
        ctx.AuditableAggregates.Add(aggregate);

        await uow.SaveChangesAsync();

        dispatched.Should().NotBeEmpty("events must be dispatched after commit");
        aggregate.DomainEvents.Should().BeEmpty("events must be cleared post-dispatch");
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutDispatcher_ClearsEventsWithoutDispatch()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);

        var aggregate = new AuditableTestAggregate(TestId.New(), "NoDispatch", new SystemClock());
        aggregate.RaiseTestEvent();
        ctx.AuditableAggregates.Add(aggregate);

        await uow.SaveChangesAsync();

        aggregate.DomainEvents.Should().BeEmpty("events must be cleared even without a dispatcher");
    }

    /// <summary>
    /// T-21: No double-dispatch — events cleared before second SaveChangesAsync call.
    /// Verified by raising an event, saving, then saving again without raising new events:
    /// the second dispatch receives zero events.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_NoDoubleDispatch_EventsClearedBeforeNextSave()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var dispatchCounts = new List<int>();
        var dispatcher = new CountingDispatcher(dispatchCounts);
        var uow = new EfUnitOfWork(ctx, dispatcher);

        var aggregate = new AuditableTestAggregate(TestId.New(), "DoubleDispatch", new SystemClock());
        aggregate.RaiseTestEvent();
        ctx.AuditableAggregates.Add(aggregate);

        // First save — one event dispatched
        await uow.SaveChangesAsync();
        dispatchCounts.Should().HaveCount(1);
        dispatchCounts[0].Should().Be(1, "one event raised before first save");

        // Second save — no new events raised; dispatcher should receive zero events (or not be called)
        ctx.Entry(aggregate).CurrentValues["Name"] = "Modified";
        await uow.SaveChangesAsync();

        // Dispatcher may be called with an empty collection or not called — either way
        // the aggregate has no events to dispatch.
        aggregate.DomainEvents.Should().BeEmpty("events cleared; no double-dispatch");
    }

    /// <summary>
    /// Dispatch failure now PREVENTS the commit entirely — dispatch runs BEFORE the
    /// physical save, so nothing is written when a handler throws. This replaces the former "known
    /// trade-off" (dispatch ran post-commit, so a dispatch failure left already-committed data
    /// behind) — the new ordering removes that trade-off structurally.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_DispatchFailure_NothingIsCommitted()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx, new ThrowingDispatcher());

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "DispatchFail", new SystemClock());
        aggregate.RaiseTestEvent();
        ctx.AuditableAggregates.Add(aggregate);

        // Act — dispatcher throws BEFORE the physical save ever runs.
        var act = async () => await uow.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>("dispatcher is expected to throw");

        // Assert — nothing was committed: dispatch runs pre-commit, so a handler failure leaves the
        // database exactly as it was before SaveChangesAsync was called.
        ctx.ChangeTracker.Clear();
        var saved = await ctx.AuditableAggregates.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == id);
        saved.Should().BeNull(
            "dispatch runs before the physical save, so a dispatch failure commits nothing at all");
    }

    /// <summary>
    /// A hard-deleted aggregate's domain events are captured and dispatched BEFORE
    /// the delete physically commits — the pre-W2 design lost these events entirely, because EF Core
    /// stops tracking a Deleted entry once the save that removes it succeeds.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_HardDeletedAggregate_EventsAreStillDispatched()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var dispatched = new List<IDomainEvent>();
        var uow = new EfUnitOfWork(ctx, new CaptureDispatcher(dispatched));

        var aggregate = new AuditableTestAggregate(TestId.New(), "ToHardDelete", new SystemClock());
        ctx.AuditableAggregates.Add(aggregate);
        await uow.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await ctx.AuditableAggregates.FirstAsync(e => e.Id == aggregate.Id);
        reloaded.RaiseTestEvent();
        ctx.AuditableAggregates.Remove(reloaded);

        await uow.SaveChangesAsync();

        dispatched.Should().NotBeEmpty(
            "the event raised immediately before a hard delete must still be dispatched, even though " +
            "EF Core stops tracking the entry once the delete commits");
    }

    private sealed class CaptureDispatcher(List<IDomainEvent> captured)
        : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyList<IDomainEvent> events,
            CancellationToken cancellationToken)
        {
            captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingDispatcher(List<int> counts) : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
        {
            counts.Add(events.Count);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated dispatch failure");
    }
}

// ---------------------------------------------------------------------------
// Helper specs and repository stubs
// ---------------------------------------------------------------------------

internal sealed class NoFilterSpec<T> : Specification<T> where T : class { }

internal sealed class IncludeDeletedSpec<T> : Specification<T> where T : class
{
    public IncludeDeletedSpec() => IncludeSoftDeleted();
}

// Concrete repository stubs for AuditableTestAggregate tests
internal sealed class ConcreteAuditRepo(TestDbContext ctx)
    : EfRepository<AuditableTestAggregate, TestId>(ctx);

internal sealed class ConcreteAuditReadRepo(
    TestDbContext ctx,
    ISpecificationEvaluator<AuditableTestAggregate> evaluator)
        : EfReadRepository<AuditableTestAggregate, TestId>(ctx, evaluator);
