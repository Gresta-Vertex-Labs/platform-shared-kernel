using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

// ---------------------------------------------------------------------------
// ITransactionalUnitOfWork integration tests
// ---------------------------------------------------------------------------

public sealed class TransactionalUnitOfWorkTests
{
    // -----------------------------------------------------------------------
    // Contract shape assertions (BCL-only in Abstractions)
    // -----------------------------------------------------------------------

    [Fact]
    public void ITransactionalUnitOfWork_Extends_IUnitOfWork()
    {
        typeof(IUnitOfWork).IsAssignableFrom(typeof(ITransactionalUnitOfWork))
            .Should().BeTrue("ITransactionalUnitOfWork must extend IUnitOfWork");
    }

    [Fact]
    public void ITransactionalUnitOfWork_Declares_BeginTransactionAsync()
    {
        // Two overloads now exist (CancellationToken; IsolationLevel?, CancellationToken) —
        // disambiguate by parameter types.
        var method = typeof(ITransactionalUnitOfWork).GetMethod(
            "BeginTransactionAsync", [typeof(CancellationToken)]);
        method.Should().NotBeNull("ITransactionalUnitOfWork must declare BeginTransactionAsync(CancellationToken)");
        method!.ReturnType.Should().Be(typeof(Task<IPersistenceTransaction>));
    }

    [Fact]
    public void IPersistenceTransaction_Declares_CommitAsync()
    {
        typeof(IPersistenceTransaction).GetMethod("CommitAsync")
            .Should().NotBeNull("IPersistenceTransaction must declare CommitAsync");
    }

    [Fact]
    public void IPersistenceTransaction_Declares_RollbackAsync()
    {
        typeof(IPersistenceTransaction).GetMethod("RollbackAsync")
            .Should().NotBeNull("IPersistenceTransaction must declare RollbackAsync");
    }

    [Fact]
    public void IPersistenceTransaction_Implements_IAsyncDisposable()
    {
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(IPersistenceTransaction))
            .Should().BeTrue("IPersistenceTransaction must extend IAsyncDisposable");
    }

    [Fact]
    public void Abstractions_Assembly_Has_NullEfCoreReference()
    {
        var assembly = typeof(IPersistenceTransaction).Assembly;
        var hasEfCore = assembly.GetReferencedAssemblies()
            .Any(r => r.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        hasEfCore.Should().BeFalse(
            "IPersistenceTransaction and ITransactionalUnitOfWork are in Abstractions which must have zero ORM dependencies");
    }

    // -----------------------------------------------------------------------
    // SQLite integration — commit path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BeginTransaction_CommitAsync_Persists_Entity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);
        var id = TestId.New();

        // Act
        await using var tx = await uow.BeginTransactionAsync();
        await ctx.TestAggregates.AddAsync(new TestAggregate(id, "CommitTest", new SystemClock()));
        await uow.SaveChangesAsync();
        await tx.CommitAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.TestAggregates.FindAsync(id);
        found.Should().NotBeNull("entity must be persisted after CommitAsync");
        found!.Name.Should().Be("CommitTest");
    }

    // -----------------------------------------------------------------------
    // SQLite integration — rollback path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BeginTransaction_RollbackAsync_DoesNot_Persist_Entity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);
        var id = TestId.New();

        // Act
        await using var tx = await uow.BeginTransactionAsync();
        await ctx.TestAggregates.AddAsync(new TestAggregate(id, "RollbackTest", new SystemClock()));
        await uow.SaveChangesAsync();
        await tx.RollbackAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.TestAggregates.FindAsync(id);
        found.Should().BeNull("entity must NOT be persisted after RollbackAsync");
    }

    // -----------------------------------------------------------------------
    // SQLite integration — DisposeAsync on uncommitted transaction
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_OnUncommittedTransaction_DoesNotThrow()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);

        Func<Task> act = async () =>
        {
            await using var tx = await uow.BeginTransactionAsync();
            // No commit or rollback — just dispose.
        };

        await act.Should().NotThrowAsync(
            "DisposeAsync on an uncommitted transaction must not throw (implicit rollback)");
    }

    // -----------------------------------------------------------------------
    // ITransactionalUnitOfWork is also IUnitOfWork (same instance)
    // -----------------------------------------------------------------------

    [Fact]
    public void EfTransactionalUnitOfWork_Implements_Both_Interfaces()
    {
        var type = typeof(EfTransactionalUnitOfWork);
        typeof(IUnitOfWork).IsAssignableFrom(type).Should().BeTrue();
        typeof(ITransactionalUnitOfWork).IsAssignableFrom(type).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ExecuteInTransactionAsync (retry-safe alternative)
    // -----------------------------------------------------------------------

    [Fact]
    public void ITransactionalUnitOfWork_Declares_ExecuteInTransactionAsync_NonGeneric()
    {
        var method = typeof(ITransactionalUnitOfWork).GetMethod("ExecuteInTransactionAsync", [typeof(Func<CancellationToken, Task>), typeof(CancellationToken)]);
        method.Should().NotBeNull("ITransactionalUnitOfWork must declare the non-generic ExecuteInTransactionAsync overload");
    }

    [Fact]
    public void ITransactionalUnitOfWork_Declares_ExecuteInTransactionAsync_Generic()
    {
        var method = typeof(ITransactionalUnitOfWork).GetMethods()
            .FirstOrDefault(m => m.Name == "ExecuteInTransactionAsync" && m.IsGenericMethodDefinition);
        method.Should().NotBeNull("ITransactionalUnitOfWork must declare the generic ExecuteInTransactionAsync<TResult> overload");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NonGeneric_CommitsAndPersistsEntity()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);
        var id = TestId.New();

        // Act
        await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "ExecTxCommit", new SystemClock()), token);
            await uow.SaveChangesAsync(token);
        });

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.TestAggregates.FindAsync(id);
        found.Should().NotBeNull("entity must be persisted after ExecuteInTransactionAsync completes");
        found!.Name.Should().Be("ExecTxCommit");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Generic_CommitsAndReturnsResult()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);
        var id = TestId.New();

        // Act
        var savedCount = await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "ExecTxGeneric", new SystemClock()), token);
            return await uow.SaveChangesAsync(token);
        });

        // Assert
        savedCount.Should().Be(1);
        ctx.ChangeTracker.Clear();
        var found = await ctx.TestAggregates.FindAsync(id);
        found.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationThrows_RollsBackAndDoesNotPersist()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);
        var id = TestId.New();

        // Act
        Func<Task> act = () => uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "ExecTxRollback", new SystemClock()), token);
            await uow.SaveChangesAsync(token);
            throw new InvalidOperationException("simulated failure after save, before commit");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — the transaction was never committed (delegate threw before CommitAsync), so
        // disposing the uncommitted IDbContextTransaction rolls back implicitly.
        ctx.ChangeTracker.Clear();
        var found = await ctx.TestAggregates.FindAsync(id);
        found.Should().BeNull("entity must NOT be persisted when the operation delegate throws before commit");
    }

    [Fact]
    public async Task BeginTransactionAsync_NoRetryStrategyConfigured_DoesNotThrow()
    {
        // Arrange — SQLite's default execution strategy never retries, so the retry-detection guard
        // must not fire for the platform's default (non-retry) configuration.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfTransactionalUnitOfWork(ctx);

        Func<Task> act = async () =>
        {
            await using var tx = await uow.BeginTransactionAsync();
        };

        await act.Should().NotThrowAsync();
    }
}
