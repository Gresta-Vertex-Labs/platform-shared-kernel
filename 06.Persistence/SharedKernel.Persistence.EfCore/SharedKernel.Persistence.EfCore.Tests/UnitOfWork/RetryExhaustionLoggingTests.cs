using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Logging;

// Reuses the retry-forcing fixtures already defined for PersistenceRetryDiagnosticListenerTests
// (AlwaysRetryStrategyFactory, FaultInjectingInterceptor, RetryDiagListenerTestDbContext/Item).
using SharedKernel.Persistence.EfCore.Tests.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// <see cref="EfUnitOfWork"/>'s
/// <c>TransientRetryExhausted</c> Warning (EventId <c>6008</c>).
/// </summary>
/// <remarks>
/// Tagged into the shared <c>"RetryDiagnostics"</c> xUnit collection — see
/// <see cref="SharedKernel.Persistence.EfCore.Tests.Diagnostics.PersistenceRetryDiagnosticListenerTests"/>'s
/// own remarks for why this test class must never run concurrently with that one (both force genuine
/// EF Core retries via the identical <c>AlwaysRetryStrategyFactory</c>/<c>FaultInjectingInterceptor</c>
/// technique, and <c>PersistenceRetryDiagnosticListener</c> observes retry events process-wide).
/// </remarks>
[Collection("RetryDiagnostics")]
public sealed class RetryExhaustionLoggingTests
{
    private static (RetryDiagListenerTestDbContext Context, FaultInjectingInterceptor Fault) CreateExhaustingContext()
    {
        // Always fails — genuine retry-limit exhaustion after AlwaysRetryStrategy's maxRetryCount.
        var faultInjector = new FaultInjectingInterceptor(failuresBeforeSuccess: int.MaxValue);

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

        var ctx = new RetryDiagListenerTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        return (ctx, faultInjector);
    }

    [Fact]
    public async Task EfUnitOfWork_SaveChangesAsync_RetryExhausted_LogsWarning_WithFallbackAttemptCount()
    {
        // Arrange
        var (ctx, _) = CreateExhaustingContext();
        await using var _1 = ctx;
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

        var inMemoryLogger = new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var uow = new SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork(
            ctx, dispatcher: null, logger: inMemoryLogger, retryOptions: null);

        // Act
        Func<Task> act = () => uow.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<RetryLimitExceededException>();

        var record = inMemoryLogger.Records.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
        record.TryGetProperty("AttemptCount", out var attemptCount).Should().BeTrue();
        attemptCount.Should().Be(1); // no TransientFaultRetryOptions supplied — documented fallback
    }

    [Fact]
    public async Task EfUnitOfWork_SaveChangesAsync_RetryExhausted_LogsWarning_WithConfiguredAttemptCount()
    {
        // Arrange
        var (ctx, _) = CreateExhaustingContext();
        await using var _1 = ctx;
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

        var inMemoryLogger = new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var retryOptions = new TransientFaultRetryOptions(MaxRetryCount: 5, MaxRetryDelay: null);
        var uow = new SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork(
            ctx, dispatcher: null, logger: inMemoryLogger, retryOptions: retryOptions);

        // Act
        Func<Task> act = () => uow.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<RetryLimitExceededException>();

        var record = inMemoryLogger.Records.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
        record.TryGetProperty("AttemptCount", out var attemptCount).Should().BeTrue();
        attemptCount.Should().Be(6); // MaxRetryCount (5) + 1
    }

    [Fact]
    public async Task EfUnitOfWork_SaveChangesAsync_Success_NeverLogsRetryExhausted()
    {
        // Arrange — no fault at all, genuinely healthy save.
        var options = new DbContextOptionsBuilder<RetryDiagListenerTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            // Each context here calls ReplaceService/AddInterceptors with fresh instances,
            // which forces EF to build a new internal service provider per context. Past 20
            // EF escalates ManyServiceProvidersCreatedWarning to an exception, which fails
            // these tests only when the full suite runs (CI), never in isolation. The extra
            // providers are intentional test isolation, so the warning is suppressed here.
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .ReplaceService<IExecutionStrategyFactory, AlwaysRetryStrategyFactory>()
                .Options;

        var userContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(
            userContext, clock);
        var softDelete = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(clock);
        var concurrency = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();

        await using var ctx = new RetryDiagListenerTestDbContext(options, new PersistenceContextDependencies(audit, softDelete, concurrency));
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

        var inMemoryLogger = new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var uow = new SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork(
            ctx, dispatcher: null, logger: inMemoryLogger, retryOptions: null);

        // Act
        var affected = await uow.SaveChangesAsync();

        // Assert
        affected.Should().Be(1);
        inMemoryLogger.Records.ShouldNotHaveLogged(new EventId(6008));
    }

    [Fact]
    public async Task EfUnitOfWork_ExecuteInTransactionAsync_RetryExhausted_LogsWarning()
    {
        // Arrange
        var (ctx, _) = CreateExhaustingContext();
        await using var _1 = ctx;
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var inMemoryLogger =
            new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var tuow = new SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork(
            ctx, dispatcher: null, logger: inMemoryLogger, retryOptions: null);

        // Act — the operation delegate always faults on its INSERT, forcing exhaustion.
        Func<Task> act = () => tuow.ExecuteInTransactionAsync(async token =>
        {
            ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });
            await ctx.SaveChangesAsync(token);
        });

        // Assert
        await act.Should().ThrowAsync<RetryLimitExceededException>();
        inMemoryLogger.Records.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
    }
}
