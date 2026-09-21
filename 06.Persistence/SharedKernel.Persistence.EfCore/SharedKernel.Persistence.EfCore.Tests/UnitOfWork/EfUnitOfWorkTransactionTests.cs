using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.Diagnostics;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// The one EF Core unit of work (P-558): <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>
/// commits, rolls back on an exception or a failed <c>Result</c>, runs pre-commit callbacks inside the
/// transaction, joins an active transaction, and — under a retrying execution strategy — replays the
/// operation from a cleared change tracker.
/// </summary>
[Collection("RetryDiagnostics")]
public sealed class EfUnitOfWorkTransactionTests
{
    [Fact]
    public void EfUnitOfWork_ImplementsTheSharedContract_AndTheContractHasNoEfCoreDependency()
    {
        typeof(IUnitOfWork).IsAssignableFrom(typeof(EfUnitOfWork)).Should().BeTrue();
        typeof(IUnitOfWork).Assembly.GetReferencedAssemblies()
            .Should().NotContain(r => r.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteInTransaction_Commits_AndSavesWithoutAnExplicitSave()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var id = TestId.New();

        await uow.ExecuteInTransactionAsync(async token =>
        {
            uow.IsTransactionActive.Should().BeTrue();
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "Commit", new SystemClock()), token);
        });

        uow.IsTransactionActive.Should().BeFalse();
        ctx.ChangeTracker.Clear();
        (await ctx.TestAggregates.FindAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteInTransaction_Generic_ReturnsTheOperationResult()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);

        var value = await uow.ExecuteInTransactionAsync(_ => Task.FromResult(42));

        value.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteInTransaction_OperationThrows_RollsBack_AndClearsTheChangeTracker()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var id = TestId.New();

        var act = () => uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "Rollback", new SystemClock()), token);
            await uow.SaveChangesAsync(token);
            throw new InvalidOperationException("simulated failure after save, before commit");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        ctx.ChangeTracker.Entries().Should().BeEmpty("nothing the failed operation staged may be saved later");
        (await ctx.TestAggregates.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteInTransaction_FailedResult_RollsBack_SkipsCallbacks_AndReturnsTheFailure()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var id = TestId.New();
        var callbackRan = false;

        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "Rejected", new SystemClock()), token);
            uow.OnBeforeCommit(_ =>
            {
                callbackRan = true;
                return Task.CompletedTask;
            });
            return Result.Failure(Error.BusinessRule("rule", "denied"));
        });

        result.IsFailure.Should().BeTrue();
        callbackRan.Should().BeFalse();
        ctx.ChangeTracker.Entries().Should().BeEmpty();
        (await ctx.TestAggregates.FindAsync(id)).Should().BeNull("a failed Result commits nothing");
    }

    [Fact]
    public async Task OnBeforeCommit_RunsAfterTheSave_InsideTheTransaction_AndItsChangesCommit()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var businessId = TestId.New();
        var callbackId = TestId.New();

        await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(businessId, "Business", new SystemClock()), token);
            uow.OnBeforeCommit(async ct =>
            {
                uow.IsTransactionActive.Should().BeTrue();
                ctx.Entry(ctx.TestAggregates.Local.Single(a => a.Id == businessId)).State
                    .Should().Be(EntityState.Unchanged, "the operation's changes are saved before callbacks run");
                await ctx.TestAggregates.AddAsync(new TestAggregate(callbackId, "Callback", new SystemClock()), ct);
            });
        });

        ctx.ChangeTracker.Clear();
        (await ctx.TestAggregates.FindAsync(businessId)).Should().NotBeNull();
        (await ctx.TestAggregates.FindAsync(callbackId)).Should().NotBeNull("a callback's own EF changes are saved before the commit");
    }

    [Fact]
    public async Task OnBeforeCommit_Throws_RollsBackEverything()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var id = TestId.New();

        var act = () => uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.TestAggregates.AddAsync(new TestAggregate(id, "Business", new SystemClock()), token);
            uow.OnBeforeCommit(_ => throw new InvalidOperationException("audit write failed"));
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("audit write failed");
        (await ctx.TestAggregates.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public void OnBeforeCommit_OutsideATransaction_Throws()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);

        var act = () => uow.OnBeforeCommit(_ => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task NestedExecuteInTransaction_JoinsTheOuterTransaction()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = new EfUnitOfWork(ctx);
        var id = TestId.New();

        var act = () => uow.ExecuteInTransactionAsync(async token =>
        {
            await uow.ExecuteInTransactionAsync(async inner =>
                await ctx.TestAggregates.AddAsync(new TestAggregate(id, "Inner", new SystemClock()), inner), token);

            uow.IsTransactionActive.Should().BeTrue("the inner call joined; it neither committed nor ended the transaction");
            throw new InvalidOperationException("outer fails after the inner call");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await ctx.TestAggregates.FindAsync(id)).Should().BeNull("the outer rollback undoes the joined inner work");
    }

    [Fact]
    public async Task RetryingStrategy_ReplaysTheOperation_FromAClearedTracker_AndCommitsOnce()
    {
        await using var ctx = CreateRetryingContext(new FaultInjectingInterceptor(failuresBeforeSuccess: 1));
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfUnitOfWork(ctx);
        var attempts = 0;
        var callbackRuns = 0;

        await uow.ExecuteInTransactionAsync(_ =>
        {
            attempts++;
            ctx.ChangeTracker.Entries().Should().BeEmpty("a retried attempt must not inherit the failed attempt's staged changes");
            ctx.Items.Add(new RetryDiagListenerTestItem { Name = "retried" });
            uow.OnBeforeCommit(_ =>
            {
                callbackRuns++;
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        attempts.Should().Be(2);
        callbackRuns.Should().Be(1, "callbacks queued by the failed attempt are discarded with it");
        ctx.ChangeTracker.Clear();
        ctx.Items.Count().Should().Be(1);
    }

    [Fact]
    public async Task RetryingStrategy_WithChangesStagedBeforeTheCall_RefusesToStart()
    {
        await using var ctx = CreateRetryingContext(new FaultInjectingInterceptor(failuresBeforeSuccess: 0));
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfUnitOfWork(ctx);
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "staged outside" });

        var act = () => uow.ExecuteInTransactionAsync(_ => Task.CompletedTask);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retrying execution strategy*");
    }

    private static RetryDiagListenerTestDbContext CreateRetryingContext(FaultInjectingInterceptor faultInjector)
    {
        var options = new DbContextOptionsBuilder<RetryDiagListenerTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .ReplaceService<IExecutionStrategyFactory, AlwaysRetryStrategyFactory>()
            .AddInterceptors(faultInjector)
            .Options;

        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit = new AuditInterceptor(TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid()), clock);

        return new RetryDiagListenerTestDbContext(
            options,
            new PersistenceContextDependencies(audit, new SoftDeleteInterceptor(clock), new ConcurrencyInterceptor()));
    }
}
