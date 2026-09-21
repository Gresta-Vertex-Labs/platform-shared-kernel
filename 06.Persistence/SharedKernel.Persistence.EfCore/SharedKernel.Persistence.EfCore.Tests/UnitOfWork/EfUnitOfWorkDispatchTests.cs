using FluentAssertions;
using NSubstitute;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// <see cref="EfUnitOfWork"/> domain-event dispatch always happens PRE-COMMIT, before the physical
/// save, whether or not a transaction is active.
/// </summary>
public sealed class EfUnitOfWorkDispatchTests
{
    private static (TestDbContext ctx, IDomainEventDispatcher dispatcher) CreateContextWithDispatcher()
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return (ctx, dispatcher);
    }

    [Fact]
    public async Task ExecuteInTransaction_DispatchesBeforeCommit_ExactlyOnce()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = EfUnitOfWork.For(ctx, dispatcher);
        var dispatchedBeforeCommit = false;

        // Act
        await uow.ExecuteInTransactionAsync(_ =>
        {
            var entity = new AuditableTestAggregate(TestId.New(), "Event Test", new SystemClock());
            entity.RaiseTestEvent();
            ctx.AuditableAggregates.Add(entity);

            uow.OnBeforeCommit(async _ =>
            {
                dispatchedBeforeCommit = dispatcher.ReceivedCalls().Any();
                await Task.CompletedTask;
            });

            return Task.CompletedTask;
        });

        // Assert
        dispatchedBeforeCommit.Should().BeTrue("dispatch happens with the save, before the pre-commit callbacks and the commit");
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_DispatchesImmediately()
    {
        // Arrange
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = EfUnitOfWork.For(ctx, dispatcher);

        var entity = new AuditableTestAggregate(TestId.New(), "Event Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act
        await uow.SaveChangesAsync();

        // Assert
        await dispatcher.Received(1).DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedResult_RollsBack_BeforeAnyDispatch()
    {
        // A failed Result short-circuits before the save, so nothing is dispatched — unlike an
        // exception thrown after a save inside the operation, which cannot retract a dispatch that
        // already fired (see DomainEventDispatchLoop's remarks).
        var (ctx, dispatcher) = CreateContextWithDispatcher();
        var uow = EfUnitOfWork.For(ctx, dispatcher);

        var result = await uow.ExecuteInTransactionAsync(_ =>
        {
            var entity = new AuditableTestAggregate(TestId.New(), "Rollback Test", new SystemClock());
            entity.RaiseTestEvent();
            ctx.AuditableAggregates.Add(entity);
            return Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied")));
        });

        result.IsFailure.Should().BeTrue();
        await dispatcher.DidNotReceive().DispatchAsync(Arg.Any<IReadOnlyList<IDomainEvent>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DomainEvents_Cleared_WithNoDispatcherRegistered()
    {
        // Arrange — no dispatcher (null)
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = EfUnitOfWork.For(ctx, null);

        var entity = new AuditableTestAggregate(TestId.New(), "Clear Test", new SystemClock());
        entity.RaiseTestEvent();
        ctx.AuditableAggregates.Add(entity);

        // Act
        await uow.SaveChangesAsync();

        // Assert
        entity.DomainEvents.Should().BeEmpty();
    }
}
