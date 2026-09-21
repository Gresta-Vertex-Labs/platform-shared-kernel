using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.Integration;

// ---------------------------------------------------------------------------
// Proves EfUnitOfWork.ExecuteInTransactionAsync works end to end against real PostgreSQL with
// Npgsql retry-on-failure genuinely enabled (UsePostgres(..., o => o.MaxRetryCount = ...)) — retry and
// transactions coexist (P-558; there is no handle-based BeginTransactionAsync any more).
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
internal sealed class TransientFaultInjectionInterceptor(int failuresBeforeSuccess) : DbCommandInterceptor
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
        builder.UsePostgres(TestNpgsqlDataSources.Get(connectionString), o =>
        {
            o.MaxRetryCount = 3;
            o.MaxRetryDelay = TimeSpan.FromMilliseconds(200);
        });
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        var options = builder.Options;

        var actorContext = new FakeAuditActorContext();
        var clock = new FakeClock();

        var audit = PersistenceContextDependencies.Create(actorContext, clock);

        return new ConcurrencyTestDbContext(options, audit);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithRetryEnabled_CompletesNormally()
    {
        // Arrange
        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync();
        var uow = EfUnitOfWork.For(ctx);

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

        (await act.Should().ThrowAsync<SharedKernel.Core.Exceptions.ConflictException>(
            "a genuine unique-constraint violation is not transient: it propagates immediately (classified), not retried away"))
            .WithInnerException<DbUpdateException>();
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
        var uow = EfUnitOfWork.For(ctx);

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
    // P-558 — retry is ON BY DEFAULT through the entry point (AddSharedKernelPostgres), and
    // IUnitOfWork.ExecuteInTransactionAsync replays the whole unit of work on a transient failure.
    // -------------------------------------------------------------------------

    private ServiceProvider BuildDiProvider(
        TransientFaultInjectionInterceptor? faultInjector = null,
        Action<PostgresProviderOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:retry"] = ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelPostgres<ConcurrencyTestDbContext>(configuration, "retry", p => p
            .ConfigureProvider(o =>
            {
                o.MaxRetryDelay = TimeSpan.FromMilliseconds(200);
                configure?.Invoke(o);
            })
            .ConfigureDbContext((_, opts) =>
            {
                if (faultInjector is not null)
                    opts.AddInterceptors(faultInjector);
            }));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddSharedKernelPostgres_EnablesRetryingExecutionStrategyByDefault()
    {
        await using var provider = BuildDiProvider();
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();

        ctx.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeTrue(
            "retry-on-failure is the default of the platform's PostgreSQL setup");
    }

    [Fact]
    public async Task AddSharedKernelPostgres_RetryDisabled_UsesNonRetryingExecutionStrategy()
    {
        await using var provider = BuildDiProvider(configure: o => o.MaxRetryCount = 0);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();

        ctx.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ViaDi_RetryOnByDefault_ReplaysWholeUnitOfWorkOnTransientFailure()
    {
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: 1);
        await using var provider = BuildDiProvider(faultInjector);
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var id = ConcurrentPgId.New();
        var runs = 0;

        await uow.ExecuteInTransactionAsync(async token =>
        {
            runs++;
            await ctx.Aggregates.AddAsync(new ConcurrentPgAggregate(id, "DiRetry", new SystemClock()), token);
        });

        runs.Should().Be(2, "the first attempt hit the injected transient fault and the whole delegate re-ran");

        await using var verifyCtx = CreateRetryEnabledContext(ConnectionString);
        (await verifyCtx.Aggregates.CountAsync(a => a.Id == id)).Should().Be(1,
            "the failed attempt rolled back and the replay committed exactly once");
    }

    [Fact]
    public async Task SaveChangesAsync_RetryExhausted_LogsTransientRetryExhaustedOnceWithConfiguredAttemptCount()
    {
        await using (var setupCtx = CreateRetryEnabledContext(ConnectionString))
        {
            await setupCtx.Database.EnsureCreatedAsync();
        }

        // Always fails — genuine retry-limit exhaustion after the configured MaxRetryCount (3).
        var faultInjector = new TransientFaultInjectionInterceptor(failuresBeforeSuccess: int.MaxValue);
        await using var provider = BuildDiProvider(faultInjector, o => o.MaxRetryCount = 3);
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConcurrencyTestDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ctx.Aggregates.Add(new ConcurrentPgAggregate(ConcurrentPgId.New(), "AlwaysFails", new SystemClock()));

        Func<Task> act = () => uow.SaveChangesAsync();

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException>();
        faultInjector.AttemptCount.Should().Be(4, "one attempt plus three retries");

        var unitOfWorkRecords = loggerFactory.GetLogger(typeof(EfUnitOfWork).FullName!).Records;
        unitOfWorkRecords.ShouldHaveLoggedCount(new EventId(6008), 1);
        var record = unitOfWorkRecords.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
        record.TryGetProperty("AttemptCount", out var attemptCount).Should().BeTrue();
        attemptCount.Should().Be(4); // MaxRetryCount (3) + 1
    }
}
