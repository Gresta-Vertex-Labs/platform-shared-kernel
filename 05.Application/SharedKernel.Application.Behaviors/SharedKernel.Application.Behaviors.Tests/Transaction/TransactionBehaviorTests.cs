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
    public async Task Handle_Success_RunsHandlerInsideTransactionThenSavesAndCommits()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var result = await behavior.Handle(new TestCommand(), () =>
        {
            sequence.Add("handler");
            unitOfWork.IsTransactionActive.Should().BeTrue("the handler runs inside the transaction");
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "handler", "transaction.savechanges", "transaction.commit");
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ResultFailure_RollsBackWithoutSaving()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var result = await behavior.Handle(
            new TestCommand(),
            () => Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        sequence.Should().Equal("transaction.begin", "transaction.rollback");
        unitOfWork.SaveChangesCallCount.Should().Be(0, "a failed Result must never reach SaveChangesAsync");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ThrownException_RollsBackAndRethrows()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var act = async () => await behavior.Handle(
            new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "transaction.rollback");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SaveChangesThrows_RollsBackAndRethrows()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence) { SimulateSaveChangesFailure = true };
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        var act = async () => await behavior.Handle(
            new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sequence.Should().Equal("transaction.begin", "transaction.savechanges", "transaction.rollback");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_TransientFailure_ReplaysTheWholeInnerPipeline()
    {
        var unitOfWork = new FakeUnitOfWork { TransientFailures = 1 };
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());
        var handlerRuns = 0;

        var result = await behavior.Handle(new TestCommand(), () =>
        {
            handlerRuns++;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handlerRuns.Should().Be(2, "a retrying strategy replays the handler, which is why handlers must be re-runnable");
        unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Nested_JoinsTheOuterTransaction()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope(isNested: true));

        var result = await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sequence.Should().BeEmpty("a nested command must not open a transaction; only the outermost command does");
        unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_TransactionAlreadyActive_JoinsItWithoutCommitting()
    {
        var sequence = new List<string>();
        var unitOfWork = new FakeUnitOfWork(sequence);
        var behavior = new TransactionBehavior<TestCommand, Result>(unitOfWork, new FakeCommandScope());

        await unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            var result = await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            unitOfWork.CommitCount.Should().Be(0, "the command joins the transaction already running");
        });

        unitOfWork.CommitCount.Should().Be(1);
        sequence.Should().Equal("transaction.begin", "transaction.savechanges", "transaction.commit");
    }
}
