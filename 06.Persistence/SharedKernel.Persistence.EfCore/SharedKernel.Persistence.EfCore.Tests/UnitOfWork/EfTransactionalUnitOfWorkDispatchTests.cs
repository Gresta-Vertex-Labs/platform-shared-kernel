using FluentAssertions;
using NSubstitute;
using SharedKernel.Domain;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// <see cref="EfTransactionalUnitOfWork"/> domain-event dispatch
/// now always happens PRE-COMMIT, inside <see cref="EfTransactionalUnitOfWork.SaveChangesAsync"/>
/// itself — the former "defer dispatch until the explicit transaction commits" special case
/// no longer exists, because dispatch runs before the physical save regardless of whether an
/// explicit transaction is active.
/// </summary>
public sealed class EfTransactionalUnitOfWorkDispatchTests
{
    private static (TestDbContext ctx, IDomainEventDispatcher dispatcher) CreateContextWithDispatcher()
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return (ctx, dispatcher);
    }

    [Fact]
    public async Task SaveChanges_WithActiveTransaction_DispatchesDuringSaveChangesAsync_NotOnCommit()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = new EfTransactionalUnitOfWork(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Event Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act — begin transaction then save.
        await using var tx = await uow.BeginTransactionAsync();
        await uow.SaveChangesAsync();

        // Assert — dispatched exactly once ALREADY, before CommitAsync is even called.
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());

        // Act — commit.
        await tx.CommitAsync();

        // Assert — CommitAsync does not dispatch again; still exactly once total.
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_DispatchesImmediately()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = new EfTransactionalUnitOfWork(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Event Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act — save without explicit transaction
        await uow.SaveChangesAsync();

        // Assert — dispatched immediately
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_WithRollback_AlreadyDispatchedEventsAreNotUndispatched()
    {
        // Because dispatch runs pre-commit (inside SaveChangesAsync), a SUBSEQUENT
        // rollback cannot "undo" a dispatch that already happened — this is the documented caveat on
        // EfPersistenceTransaction.RollbackAsync. A handler with only in-process, same-DbContext
        // effects rolls back atomically with everything else; a handler with a genuinely external
        // effect must never be registered as an IDomainEventDispatcher handler for exactly this
        // reason (see DomainEventDispatchLoop's remarks).
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = new EfTransactionalUnitOfWork(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Rollback Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act
        await using var tx = await uow.BeginTransactionAsync();
        await uow.SaveChangesAsync();
        await tx.RollbackAsync();

        // Assert — the dispatch that happened during SaveChangesAsync already fired; rollback does
        // not (and cannot) retract it.
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DomainEvents_Cleared_Regardless_Of_DispatchPath()
    {
        // Arrange — no dispatcher (null)
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx, null);

        var entity = new AuditableTestAggregate(TestId.New(), "Clear Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act — save without transaction, no dispatcher
        await uow.SaveChangesAsync();

        // Assert — events cleared
        entity.DomainEvents.Should().BeEmpty();
    }
}
