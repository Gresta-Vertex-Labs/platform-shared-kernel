using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

/// <summary>
/// Serializes every test class that forces genuine EF Core execution-strategy retries via
/// <see cref="AlwaysRetryStrategyFactory"/>/<see cref="FaultInjectingInterceptor"/> — see
/// <see cref="PersistenceRetryDiagnosticListenerTests"/>'s own remarks for why this is required.
/// </summary>
[CollectionDefinition("RetryDiagnostics")]
public sealed class RetryDiagnosticsCollection;

/// <summary>
/// <see cref="PersistenceRetryDiagnosticListener"/>'s
/// <c>TransientRetryAttempt</c> Warning (EventId <c>6007</c>) and its opt-in DI registration.
/// </summary>
/// <remarks>
/// <see cref="PersistenceRetryDiagnosticListener"/> subscribes to
/// <see cref="System.Diagnostics.DiagnosticListener.AllListeners"/> process-wide — every EF Core
/// retry event raised ANYWHERE in the process during this test's lifetime is observable to it, not
/// just retries from this test's own <see cref="System.Data.Common.DbConnection"/>. Tagged into the
/// shared <c>"RetryDiagnostics"</c> xUnit collection (alongside
/// <see cref="SharedKernel.Persistence.EfCore.Tests.UnitOfWork.RetryExhaustionLoggingTests"/>, the
/// only other test class that forces genuine EF Core retries via the identical
/// <see cref="AlwaysRetryStrategyFactory"/>/<see cref="FaultInjectingInterceptor"/> technique) so
/// xUnit never runs them concurrently — a real cross-test count-inflation failure was observed
/// running the two in parallel before this collection tag was added.
/// </remarks>
[Collection("RetryDiagnostics")]
public sealed class PersistenceRetryDiagnosticListenerTests
{
    [Fact]
    public void WithTransientFaultRetry_RegistersPersistenceRetryDiagnosticListener_AsHostedService()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<RetryDiagListenerTestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithTransientFaultRetry()
            .Build();

        var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>()
            .OfType<PersistenceRetryDiagnosticListener>()
            .Should().ContainSingle();
    }

    [Fact]
    public void OmittingWithTransientFaultRetry_NeverRegistersPersistenceRetryDiagnosticListener()
    {
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<RetryDiagListenerTestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        var provider = services.BuildServiceProvider();
        provider.GetServices<IHostedService>()
            .OfType<PersistenceRetryDiagnosticListener>()
            .Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ObservesRealExecutionStrategyRetryingEvent_LogsWarning_PerAttempt()
    {
        // Arrange — a genuine EF Core retrying ExecutionStrategy (SQLite, provider-neutral technique
        // already proven for this package's own retry tests) whose ShouldRetryOn always retries a
        // simulated transient InvalidOperationException, verified to publish real
        // Microsoft.EntityFrameworkCore.Infrastructure.ExecutionStrategyRetrying DiagnosticListener
        // events (confirmed via direct compilation against the real EF Core 10.0.5 package before
        // writing this test).
        var inMemoryLogger = new InMemoryLogger<PersistenceRetryDiagnosticListener>();
        var listener = new PersistenceRetryDiagnosticListener(inMemoryLogger);
        await listener.StartAsync(CancellationToken.None);

        try
        {
            var faultInjector = new FaultInjectingInterceptor(failuresBeforeSuccess: 2);

            var options = new DbContextOptionsBuilder<RetryDiagListenerTestDbContext>()
                .UseSqlite("DataSource=:memory:")
            // Each context here calls ReplaceService/AddInterceptors with fresh instances,
            // which forces EF to build a new internal service provider per context. Past 20
            // EF escalates ManyServiceProvidersCreatedWarning to an exception, which fails
            // these tests only when the full suite runs (CI), never in isolation. The extra
            // providers are intentional test isolation, so the warning is suppressed here.
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .ReplaceService<IExecutionStrategyFactory, AlwaysRetryStrategyFactory>()
                .AddInterceptors(faultInjector)
                .Options;

            var userContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
            var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
            var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(
                userContext, clock);
            var softDelete = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(clock);
            var concurrency = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();

            using var ctx = new RetryDiagListenerTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
            await ctx.Database.OpenConnectionAsync();
            await ctx.Database.EnsureCreatedAsync();
            ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

            // Act — succeeds on the 3rd attempt (2 injected failures, 2 retries observed).
            await ctx.SaveChangesAsync();

            // Assert
            var records = inMemoryLogger.Records;
            records.ShouldHaveLogged(new EventId(6007), LogLevel.Warning);
            records.Count(r => r.EventId.Id == 6007).Should().Be(2);
            records.ShouldHaveLoggedWithProperty(new EventId(6007), "AttemptNumber", 1);
            records.ShouldHaveLoggedWithProperty(new EventId(6007), "AttemptNumber", 2);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }
}

// ---------------------------------------------------------------------------
// Test-local plain DbContext (this test targets the diagnostic listener in isolation —
// no SharedKernelDbContext/persistence interceptors are needed).
// ---------------------------------------------------------------------------

public sealed class RetryDiagListenerTestDbContext : SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext
{
    public DbSet<RetryDiagListenerTestItem> Items => Set<RetryDiagListenerTestItem>();

    public RetryDiagListenerTestDbContext(
        DbContextOptions<RetryDiagListenerTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetryDiagListenerTestItem>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedOnAdd();
        });
    }
}

public sealed class RetryDiagListenerTestItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Retry-forcing execution strategy — mirrors the technique independently verified against the
// real EF Core 10.0.5 package before this test was written.
// ---------------------------------------------------------------------------

internal sealed class AlwaysRetryStrategy : ExecutionStrategy
{
    public AlwaysRetryStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 5, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is InvalidOperationException;
}

internal sealed class AlwaysRetryStrategyFactory : IExecutionStrategyFactory
{
    private readonly ExecutionStrategyDependencies _dependencies;

    public AlwaysRetryStrategyFactory(ExecutionStrategyDependencies dependencies) => _dependencies = dependencies;

    public IExecutionStrategy Create() => new AlwaysRetryStrategy(_dependencies);
}

// Throws a simulated transient failure for the first `failuresBeforeSuccess` INSERT attempts,
// then lets subsequent attempts through — both the reader and non-query paths are overridden
// since SQLite/Npgsql route store-generated-value writes through ExecuteReader, not
// ExecuteNonQuery (a documented gotcha in this domain's own test-writing history).
internal sealed class FaultInjectingInterceptor : DbCommandInterceptor
{
    private readonly int _failuresBeforeSuccess;
    private int _attempts;

    public FaultInjectingInterceptor(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

    public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result)
    {
        MaybeThrow(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken ct = default)
    {
        MaybeThrow(command);
        return await base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    private void MaybeThrow(System.Data.Common.DbCommand command)
    {
        if (!command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            return;

        if (Interlocked.Increment(ref _attempts) <= _failuresBeforeSuccess)
            throw new InvalidOperationException("Simulated transient database failure.");
    }
}
