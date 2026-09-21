using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_IncrementsCallCount_OncePerCall()
    {
        var unitOfWork = new FakeUnitOfWork();

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SaveChangesAsync_ReturnsConfiguredSaveChangesResult()
    {
        var unitOfWork = new FakeUnitOfWork { SaveChangesResult = 42 };

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SaveChangesAsync_DefaultSaveChangesResult_IsOne()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_ThrowsInvalidOperationException()
    {
        var unitOfWork = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_StillIncrementsCallCount()
    {
        var unitOfWork = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));

        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Success_SavesRunsCallbacksAndCommits()
    {
        var unitOfWork = new FakeUnitOfWork();
        var callbackRan = false;

        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            Assert.True(unitOfWork.IsTransactionActive);
            unitOfWork.OnBeforeCommit(_ =>
            {
                callbackRan = true;
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        Assert.True(callbackRan);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.False(unitOfWork.IsTransactionActive);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_FailedResult_RollsBack_WithoutSavingOrRunningCallbacks()
    {
        var unitOfWork = new FakeUnitOfWork();
        var callbackRan = false;

        var result = await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            unitOfWork.OnBeforeCommit(_ =>
            {
                callbackRan = true;
                return Task.CompletedTask;
            });
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                SharedKernel.Primitives.Errors.Error.BusinessRule("rule", "denied")));
        });

        Assert.True(result.IsFailure);
        Assert.False(callbackRan);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Equal(0, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Exception_RollsBackAndRethrows()
    {
        var unitOfWork = new FakeUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.False(unitOfWork.IsTransactionActive);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFailures_ReplayTheOperation()
    {
        var unitOfWork = new FakeUnitOfWork { TransientFailures = 2 };
        var runs = 0;

        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            runs++;
            return Task.CompletedTask;
        });

        Assert.Equal(3, runs);
        Assert.Equal(3, unitOfWork.TransactionCount);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Nested_JoinsTheOuterTransaction()
    {
        var unitOfWork = new FakeUnitOfWork();

        await unitOfWork.ExecuteInTransactionAsync(_ => unitOfWork.ExecuteInTransactionAsync(_ => Task.CompletedTask));

        Assert.Equal(1, unitOfWork.TransactionCount);
        Assert.Equal(1, unitOfWork.CommitCount);
    }

    [Fact]
    public void OnBeforeCommit_OutsideATransaction_Throws()
    {
        var unitOfWork = new FakeUnitOfWork();

        Assert.Throws<InvalidOperationException>(() => unitOfWork.OnBeforeCommit(_ => Task.CompletedTask));
    }
}
