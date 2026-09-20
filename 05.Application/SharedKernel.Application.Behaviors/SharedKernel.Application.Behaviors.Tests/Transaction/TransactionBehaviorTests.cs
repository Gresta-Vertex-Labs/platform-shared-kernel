using FluentAssertions;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Transaction;

public sealed class TransactionBehaviorTests
{
    private sealed record TestCommand : ICommand;

    [Fact]
    public async Task Handle_Success_CommitsExactlyOnceAfterHandlerReturns()
    {
        var unitOfWork = new FakeUnitOfWork();
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());
        var handlerRan = false;

        var result = await behavior.Handle(new TestCommand(), () =>
        {
            handlerRan = true;
            unitOfWork.CommitCount.Should().Be(0, "the commit must happen after next() returns, not before");
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        handlerRan.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ResultFailure_NeverCommits()
    {
        var unitOfWork = new FakeUnitOfWork();
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var result = await behavior.Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ThrownException_NeverCommitsAndRethrows()
    {
        var unitOfWork = new FakeUnitOfWork();
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var act = async () => await behavior.Handle(new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Nested_SkipsCommitAndCallsNextDirectly()
    {
        var unitOfWork = new FakeUnitOfWork();
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope(isNested: true));

        var result = await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.CommitCount.Should().Be(0, "only the outermost command commits");
    }

    // ---- Transactional capability (ITransactionalUnitOfWork) ----

    [Fact]
    public async Task Handle_TransactionalUnitOfWork_Success_BeginsBeforeNextThenSavesThenCommits()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var result = await behavior.Handle(new TestCommand(), () =>
        {
            sequence.Add("handler");
            unitOfWork.BeginTransactionCallCount.Should().Be(1, "the transaction must open before next() runs");
            unitOfWork.LastTransaction!.IsCommitted.Should().BeFalse();
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "handler", "transaction.savechanges", "transaction.commit");
        unitOfWork.LastTransaction!.IsCommitted.Should().BeTrue();
        unitOfWork.LastTransaction.IsRolledBack.Should().BeFalse();
        unitOfWork.LastTransaction.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TransactionalUnitOfWork_ResultFailure_RollsBackWithoutSaving()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var result = await behavior.Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "transaction.rollback");
        unitOfWork.SaveChangesCallCount.Should().Be(0, "a failed Result must never reach SaveChangesAsync");
        unitOfWork.LastTransaction!.IsRolledBack.Should().BeTrue();
        unitOfWork.LastTransaction.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TransactionalUnitOfWork_ThrownException_RollsBackAndRethrows()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var act = async () => await behavior.Handle(
            new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "transaction.rollback");
        unitOfWork.SaveChangesCallCount.Should().Be(0);
        unitOfWork.LastTransaction!.IsRolledBack.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TransactionalUnitOfWork_SaveChangesThrows_RollsBackAndRethrows()
    {
        // The scenario this capability exists to make safe: a business write fails AFTER something
        // else (an audit record, in production) has already been staged inside the same transaction —
        // neither may survive.
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence) { SimulateSaveChangesFailure = true };
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var act = async () => await behavior.Handle(
            new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "transaction.savechanges", "transaction.rollback");
        unitOfWork.LastTransaction!.IsRolledBack.Should().BeTrue();
        unitOfWork.LastTransaction.IsCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TransactionalUnitOfWork_Nested_SkipsTransactionEntirely()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeTransactionalUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope(isNested: true));

        var result = await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sequence.Should().BeEmpty("a nested command must not open a transaction at all — only the outermost command does");
        unitOfWork.BeginTransactionCallCount.Should().Be(0);
        unitOfWork.SaveChangesCallCount.Should().Be(0);
    }
}
