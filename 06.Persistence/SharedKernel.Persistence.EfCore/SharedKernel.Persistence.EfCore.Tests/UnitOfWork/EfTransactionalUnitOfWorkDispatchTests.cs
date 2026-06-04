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
/// T-31: EfTransactionalUnitOfWork double-dispatch fix tests (P-105 Fix 1).
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
    public async Task SaveChanges_WithActiveTransaction_DoesNotDispatchUntilCommit()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = new EfTransactionalUnitOfWork(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Event Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act — begin transaction then save (should NOT dispatch yet)
        await using var tx = await uow.BeginTransactionAsync();
        await uow.SaveChangesAsync();

        // Assert — not dispatched during SaveChangesAsync while transaction active
        await dispatcher.DidNotReceive().DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());

        // Act — commit
        await tx.CommitAsync();

        // Assert — dispatched exactly once after commit
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
    public async Task SaveChanges_WithRollback_DoesNotDispatch()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = new EfTransactionalUnitOfWork(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Rollback Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act
        await using var tx = await uow.BeginTransactionAsync();
        await uow.SaveChangesAsync();
        await tx.RollbackAsync();

        // Assert — never dispatched
        await dispatcher.DidNotReceive().DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
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
