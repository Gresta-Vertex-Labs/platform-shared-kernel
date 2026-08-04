using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeUnitOfWork"/> (the <c>06.Persistence</c>-shaped one, in
/// <c>SharedKernel.Testing.Persistence</c>) — <c>SaveChangesAsync</c>'s call-count increment,
/// result pass-through, and simulated-failure throw; independence from a separately-constructed
/// <see cref="FakeRepository{TAggregate, TId}"/>; <c>BeginTransactionAsync</c>; both
/// <c>ExecuteInTransactionAsync</c> overloads; and <c>Reset</c>.
/// </summary>
public sealed class FakeUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_IncrementsCallCount_OnEveryCall()
    {
        var uow = new FakeUnitOfWork();

        await uow.SaveChangesAsync();
        await uow.SaveChangesAsync();
        await uow.SaveChangesAsync();

        Assert.Equal(3, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task SaveChangesAsync_ReturnsConfiguredSaveChangesResult()
    {
        var uow = new FakeUnitOfWork { SaveChangesResult = 42 };

        Assert.Equal(42, await uow.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_Throws()
    {
        var uow = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_SimulateFailure_StillIncrementsCallCount()
    {
        var uow = new FakeUnitOfWork { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.SaveChangesAsync());

        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task SaveChangesAsync_HasNoObservableInteraction_WithASeparatelyConstructedFakeRepository()
    {
        // Mirrors T-62's established "no shared backing store between the two fakes" independence
        // proof: FakeUnitOfWork and FakeRepository<TAggregate,TId> are fully independent types with
        // no constructor coupling — a repository write is immediate (no ChangeTracker-style staging),
        // and SaveChangesAsync neither reads nor mutates it in any way.
        var order = new TestSoftDeletableOrder(Guid.NewGuid(), "Ada", 1m, 0, new FakeClock());
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id);
        var uow = new FakeUnitOfWork();

        await repo.AddAsync(order);
        await uow.SaveChangesAsync();

        // The repository write was already visible before SaveChangesAsync was ever called, and
        // remains the only thing that changed — SaveChangesAsync is a pure counter.
        Assert.NotNull(await repo.GetByIdAsync(order.Id));
        Assert.Equal(1, uow.SaveChangesCallCount);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task BeginTransactionAsync_ReturnsAWorkingFakePersistenceTransaction_AndIncrementsTransactionCount()
    {
        var uow = new FakeUnitOfWork();

        var transaction = await uow.BeginTransactionAsync();

        Assert.IsType<FakePersistenceTransaction>(transaction);
        Assert.Equal(1, uow.TransactionCount);

        await transaction.CommitAsync(); // proves the returned handle is genuinely usable
    }

    [Fact]
    public async Task BeginTransactionAsync_CalledTwice_IncrementsTransactionCountTwice_ReturnsDistinctInstances()
    {
        var uow = new FakeUnitOfWork();

        var first = await uow.BeginTransactionAsync();
        var second = await uow.BeginTransactionAsync();

        Assert.Equal(2, uow.TransactionCount);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NonGeneric_InvokesDelegateExactlyOnce()
    {
        var uow = new FakeUnitOfWork();
        var callCount = 0;

        await uow.ExecuteInTransactionAsync(_ =>
        {
            callCount++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NonGeneric_PropagatesTheDelegatesException()
    {
        var uow = new FakeUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteInTransactionAsync(_ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Generic_InvokesDelegateExactlyOnce_AndReturnsItsResult()
    {
        var uow = new FakeUnitOfWork();
        var callCount = 0;

        var result = await uow.ExecuteInTransactionAsync(_ =>
        {
            callCount++;
            return Task.FromResult(99);
        });

        Assert.Equal(1, callCount);
        Assert.Equal(99, result);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Generic_PropagatesTheDelegatesException()
    {
        var uow = new FakeUnitOfWork();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uow.ExecuteInTransactionAsync<int>(_ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Reset_ClearsCountsAndRestoresDefaults()
    {
        var uow = new FakeUnitOfWork { SaveChangesResult = 7, SimulateFailure = true };
        await uow.BeginTransactionAsync();

        uow.Reset();

        Assert.Equal(0, uow.SaveChangesCallCount);
        Assert.Equal(0, uow.TransactionCount);
        Assert.Equal(0, uow.SaveChangesResult);
        Assert.False(uow.SimulateFailure);
    }
}

/// <summary>
/// Proves <see cref="FakePersistenceTransaction"/> — double-commit/double-rollback/commit-after-dispose
/// <see cref="InvalidOperationException"/> guards, and idempotent <c>DisposeAsync</c>.
/// </summary>
public sealed class FakePersistenceTransactionTests
{
    [Fact]
    public async Task CommitAsync_Succeeds_SetsIsCommitted()
    {
        var transaction = new FakePersistenceTransaction();

        await transaction.CommitAsync();

        Assert.True(transaction.IsCommitted);
    }

    [Fact]
    public async Task CommitAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        var transaction = new FakePersistenceTransaction();
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
    }

    [Fact]
    public async Task RollbackAsync_Succeeds_SetsIsRolledBack()
    {
        var transaction = new FakePersistenceTransaction();

        await transaction.RollbackAsync();

        Assert.True(transaction.IsRolledBack);
    }

    [Fact]
    public async Task RollbackAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        var transaction = new FakePersistenceTransaction();
        await transaction.RollbackAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.RollbackAsync());
    }

    [Fact]
    public async Task CommitAsync_AfterRollback_ThrowsInvalidOperationException()
    {
        var transaction = new FakePersistenceTransaction();
        await transaction.RollbackAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
    }

    [Fact]
    public async Task RollbackAsync_AfterCommit_ThrowsInvalidOperationException()
    {
        var transaction = new FakePersistenceTransaction();
        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.RollbackAsync());
    }

    [Fact]
    public async Task CommitAsync_AfterDispose_ThrowsInvalidOperationException()
    {
        var transaction = new FakePersistenceTransaction();
        await transaction.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        var transaction = new FakePersistenceTransaction();

        await transaction.DisposeAsync();
        await transaction.DisposeAsync(); // must not throw

        Assert.True(transaction.IsDisposed);
    }
}
