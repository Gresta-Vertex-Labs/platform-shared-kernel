using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

// ---------------------------------------------------------------------------
// Proves the required two-call pairing (UsePostgreSQL(..., maxRetryCount) +
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

/// <remarks>
/// Shares the <see cref="PostgreSqlContainerFixture"/> registered by
/// <see cref="PostgreSqlTestCollection"/> instead of starting its own dedicated container per test
/// method — see <see cref="ConcurrencyIntegrationTests"/>'s identical remark for why a
/// uniquely-named database is targeted rather than the fixture's shared default database (this class
/// reuses <see cref="ConcurrencyTestDbContext"/>/<see cref="ConcurrentPgAggregate"/> from that same
/// file, but deliberately targets its OWN database rather than sharing
/// <see cref="ConcurrencyIntegrationTests"/>'s, preserving the same degree of isolation the two
/// classes had when each ran against a fully independent container).
/// </remarks>
[Collection("PostgreSQL")]
public sealed class TransientFaultRetryIntegrationTests
{
    private const string DatabaseName = "sk_persistence_transient_retry";

    private readonly PostgreSqlContainerFixture _fixture;

    public TransientFaultRetryIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private static ConcurrencyTestDbContext CreateRetryEnabledContext(
        string connectionString, params DbCommandInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgreSQL(connectionString, maxRetryCount: 3);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();

        var audit = new AuditInterceptor(actorContext, clock);
        var softDelete = new SoftDeleteInterceptor(actorContext, clock);
        var concurrency = new ConcurrencyInterceptor();

        return new ConcurrencyTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
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

    // -------------------------------------------------------------------------
    // T-100 — TransientRetryAttempt (6007) / TransientRetryExhausted (6008)
    // structured-logging proofs against a REAL PostgreSQL Testcontainer, reusing this class's own
    // T-79 injected-transient-fault technique. The internal PersistenceRetryDiagnosticListener has
    // no InternalsVisibleTo grant to this project — it is exercised purely through its public
    // IHostedService/ILogger<T> DI surface, resolving its captured records via 16.Testing's
    // InMemoryLoggerFactory category-string lookup (the type's own full CLR name), never by
    // naming the internal type directly.
    // -------------------------------------------------------------------------

    private const string RetryDiagnosticListenerCategory =
        "SharedKernel.Persistence.EfCore.Diagnostics.PersistenceRetryDiagnosticListener";

    [Fact]
    public async Task SaveChangesAsync_WithTransientFaultRetryLogging_GenuineTransientFault_LogsWarningPerRetryAttempt()
    {
        // Arrange — schema first, via a context with no fault interceptor attached.
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 2);

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts =>
                opts.UsePostgreSQL(ConnectionString, maxRetryCount: 3).AddInterceptors(faultInjector))
                    .WithTransientFaultRetry(maxRetryCount: 3)
                        .Build();

        await using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();

            ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "LoggedRetrySuccess", new SystemClock()));

            // Act — succeeds on the 3rd attempt (2 injected failures, 2 retries observed).
            await ctx.SaveChangesAsync();

            // Assert
            var records = loggerFactory.GetLogger(RetryDiagnosticListenerCategory).Records;
            records.ShouldHaveLoggedCount(new EventId(6007), 2);
            records.ShouldHaveLoggedWithProperty(new EventId(6007), "AttemptNumber", 1);
            records.ShouldHaveLoggedWithProperty(new EventId(6007), "AttemptNumber", 2);
        }
        finally
        {
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithTransientFaultRetryLogging_NoFaultInjected_NeverLogsTransientRetryAttempt()
    {
        // Arrange
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts =>
                opts.UsePostgreSQL(ConnectionString, maxRetryCount: 3))
                    .WithTransientFaultRetry(maxRetryCount: 3)
                        .Build();

        await using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();

            ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "NoRetryNeeded", new SystemClock()));

            // Act — no fault; succeeds on the very first attempt.
            await ctx.SaveChangesAsync();

            // Assert — silent when zero retries occur.
            loggerFactory.GetLogger(RetryDiagnosticListenerCategory).Records.ShouldNotHaveLogged(new EventId(6007));
        }
        finally
        {
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithTransientFaultRetryLogging_RetryExhausted_Logs6007PerAttemptAnd6008Once_DistinctEventIds_BeforeExceptionPropagates()
    {
        // Arrange
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        // Always fails — genuine retry-limit exhaustion after the configured maxRetryCount (3).
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: int.MaxValue);

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services
            .AddSharedKernelEfCore<ConcurrencyTestDbContext>(opts =>
                opts.UsePostgreSQL(ConnectionString, maxRetryCount: 3).AddInterceptors(faultInjector))
                    .WithTransientFaultRetry(maxRetryCount: 3)
                        .Build();

        await using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        try
        {
            await using var scope = provider.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "AlwaysFails", new SystemClock()));

            // Act
            Func<Task> act = () => uow.SaveChangesAsync();

            // Assert
            await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException>();

            var retryListenerRecords = loggerFactory.GetLogger(RetryDiagnosticListenerCategory).Records;
            retryListenerRecords.Count(r => r.EventId.Id == 6007).Should().Be(3); // one per retry (maxRetryCount == 3)

            var unitOfWorkRecords = loggerFactory.GetLogger(typeof(EfUnitOfWork).FullName!).Records;
            unitOfWorkRecords.ShouldHaveLoggedCount(new EventId(6008), 1);
            var record = unitOfWorkRecords.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
            record.TryGetProperty("AttemptCount", out var attemptCount).Should().BeTrue();
            attemptCount.Should().Be(4); // MaxRetryCount (3) + 1

            // Distinct EventIds, on distinct loggers, for the SAME operation.
            unitOfWorkRecords.ShouldNotHaveLogged(new EventId(6007));
            retryListenerRecords.ShouldNotHaveLogged(new EventId(6008));
        }
        finally
        {
            foreach (var hostedService in hostedServices)
                await hostedService.StopAsync(CancellationToken.None);
        }
    }
}
