using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

// ---------------------------------------------------------------------------
// WO-051/P-320 — proves the required two-call pairing (UsePostgreSQL(..., maxRetryCount) +
// the retry-safety guard on EfTransactionalUnitOfWork.BeginTransactionAsync) against real
// PostgreSQL: when Npgsql retry-on-failure is genuinely enabled, BeginTransactionAsync must throw
// an actionable InvalidOperationException directing the caller to ExecuteInTransactionAsync, which
// must itself still complete normally.
// ---------------------------------------------------------------------------

/// <summary>
/// Throws a genuine <see cref="TimeoutException"/> — a type every EF Core relational provider's
/// retrying execution strategy (including Npgsql's) explicitly classifies as transient — on the
/// first <paramref name="failuresBeforeSuccess"/> command executions, then lets subsequent attempts
/// through. Used to prove transparent retry (T-79/T-82), never an arbitrary exception type.
/// </summary>
/// <remarks>
/// Overrides BOTH the NonQuery and Reader execution paths (sharing one attempt counter): the
/// <c>ConcurrentPgAggregate</c>/<c>IHasConcurrency</c> fixture's <c>RowVersion</c> is bound to the
/// real <c>xmin</c> system column with <c>ValueGenerated.OnAddOrUpdate</c> (<c>XminConcurrencyTokenConvention</c>),
/// so its INSERT/UPDATE statements need the generated value read back via a PostgreSQL
/// <c>RETURNING</c> clause — Npgsql's EF Core provider executes such modification commands through
/// <c>ExecuteReader</c>, not <c>ExecuteNonQuery</c>. Covering both paths keeps this interceptor
/// correct regardless of which one a given command takes.
/// </remarks>
file sealed class TransientFaultInjectionInterceptor(int failuresBeforeSuccess) : DbCommandInterceptor
{
    private int _attempts;

    public int AttemptCount => _attempts;

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        MaybeThrow();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        MaybeThrow();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        MaybeThrow();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        MaybeThrow();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void MaybeThrow()
    {
        if (Interlocked.Increment(ref _attempts) <= failuresBeforeSuccess)
            throw new TimeoutException("Simulated transient fault for retry testing.");
    }
}

[Collection("PostgreSQL")]
public sealed class TransientFaultRetryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static ConcurrencyTestDbContext CreateRetryEnabledContext(
        string connectionString, params DbCommandInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgreSQL(connectionString, maxRetryCount: 3);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        var options = builder.Options;

        var userContext = Substitute.For<IUserContext>();
        userContext.IsAuthenticated.Returns(true);
        userContext.UserId.Returns(Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var serviceOptions = Options.Create(new PersistenceServiceOptions());

        var audit = new AuditInterceptor(userContext, clock, serviceOptions);
        var softDelete = new SoftDeleteInterceptor(userContext, clock, serviceOptions);
        var concurrency = new ConcurrencyInterceptor();

        return new ConcurrencyTestDbContext(options, audit, softDelete, concurrency);
    }

    [Fact]
    public async Task BeginTransactionAsync_WithRetryEnabled_ThrowsActionableInvalidOperationException()
    {
        // Arrange
        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfTransactionalUnitOfWork(ctx);

        // Act
        Func<Task> act = async () => await uow.BeginTransactionAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(nameof(EfTransactionalUnitOfWork.ExecuteInTransactionAsync));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithRetryEnabled_CompletesNormally()
    {
        // Arrange
        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfTransactionalUnitOfWork(ctx);

        var id = ConcurrentPgId.New();

        // Act — the retry-safe path must still work end to end with genuine Npgsql retry enabled.
        await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.Aggregates.AddAsync(
                new ConcurrentPgAggregate(id, "RetrySafe", new SystemClock()), token);
            await uow.SaveChangesAsync(token);
        });

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.Aggregates.FindAsync(id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("RetrySafe");
    }

    // -------------------------------------------------------------------------
    // T-79 — transparent retry on a GENUINE transient fault, plus a control proving a
    // genuinely non-transient failure (unique-constraint violation) propagates immediately.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_WithRetryEnabled_GenuineTransientFault_TransparentlyRetriesAndSucceeds()
    {
        // Arrange — create the schema first via a context WITHOUT the fault interceptor, so the
        // DDL commands never consume the injected-failure budget.
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 2);
        await using var ctx = CreateRetryEnabledContext(ConnectionString, faultInjector);

        var id = ConcurrentPgId.New();
        ctx.Aggregates.Add(new ConcurrentPgAggregate(id, "TransientRetrySuccess", new SystemClock()));

        // Act — SaveChangesAsync is automatically wrapped by the configured retrying execution
        // strategy. The first two INSERT attempts fail with a genuinely-transient TimeoutException
        // (the exact type Npgsql's retry classifier recognizes); the third succeeds.
        await ctx.SaveChangesAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.Aggregates.FindAsync(id);
        found.Should().NotBeNull(
            "the operation must transparently retry past the injected transient faults and ultimately succeed");
        found!.Name.Should().Be("TransientRetrySuccess");
        faultInjector.AttemptCount.Should().BeGreaterThanOrEqualTo(3,
            "at least 2 failed attempts plus 1 successful attempt must have been made");
    }

    [Fact]
    public async Task SaveChangesAsync_WithRetryEnabled_GenuinelyNonTransientFailure_PropagatesImmediately_NoRetry()
    {
        // Control test — a real PostgreSQL unique-constraint violation is NOT transient and must
        // propagate on the first attempt, never silently retried away.
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        var id = ConcurrentPgId.New();

        ctx.Aggregates.Add(new ConcurrentPgAggregate(id, "First", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Force a genuine duplicate-primary-key INSERT (unique_violation, SqlState 23505).
        ctx.Add(new ConcurrentPgAggregate(id, "DuplicatePrimaryKey", new SystemClock()));

        Func<Task> act = () => ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "a genuine unique-constraint violation is not transient and must propagate immediately, not be retried away");
    }

    // -------------------------------------------------------------------------
    // T-82 — ExecuteInTransactionAsync retry-under-failure: no duplicate/partial commit across a
    // retried attempt.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteInTransactionAsync_TransientFaultOnFirstAttempt_RetriesWithoutDuplicateOrPartialCommit()
    {
        // Arrange
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 1);
        await using var ctx = CreateRetryEnabledContext(ConnectionString, faultInjector);
        var uow = new EfTransactionalUnitOfWork(ctx);

        var id = ConcurrentPgId.New();
        // The entity is constructed ONCE, outside the delegate — the delegate itself only adds it
        // to the ChangeTracker if not already tracked, so re-running the WHOLE delegate on retry
        // (per ExecuteInTransactionAsync's documented contract) is safe and does not attempt to
        // track a second conflicting instance with the same key.
        var entity = new ConcurrentPgAggregate(id, "RetryNoDuplicate", new SystemClock());

        // Act — the first attempt's transaction fails mid-flight on a genuinely-transient fault and
        // rolls back (IDbContextTransaction dispose-without-commit); the whole delegate re-runs,
        // including a FRESH BeginTransactionAsync, and the second attempt commits successfully.
        await uow.ExecuteInTransactionAsync(async token =>
        {
            if (ctx.Entry(entity).State == EntityState.Detached)
                await ctx.Aggregates.AddAsync(entity, token);
            await uow.SaveChangesAsync(token);
        });

        // Assert — exactly ONE row exists for this id, proving the failed first attempt's
        // transaction fully rolled back before the retry began, rather than leaving a partial or
        // duplicated commit behind.
        await using var verifyCtx = CreateRetryEnabledContext(ConnectionString);
        var count = await verifyCtx.Aggregates.CountAsync(a => a.Id == id);
        count.Should().Be(1,
            "the failed first attempt must fully roll back — no duplicate/partial commit after a successful retry");
        faultInjector.AttemptCount.Should().BeGreaterThanOrEqualTo(2,
            "at least one failed attempt plus one successful attempt must have been made");
    }
}
