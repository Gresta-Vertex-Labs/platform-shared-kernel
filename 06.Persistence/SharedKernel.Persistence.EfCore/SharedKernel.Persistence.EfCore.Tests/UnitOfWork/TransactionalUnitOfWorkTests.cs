using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

// ---------------------------------------------------------------------------
// T-27 — ITransactionalUnitOfWork integration tests (P-099 Part 2)
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
        var method = typeof(ITransactionalUnitOfWork).GetMethod("BeginTransactionAsync");
        method.Should().NotBeNull("ITransactionalUnitOfWork must declare BeginTransactionAsync");
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
}
