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
}
