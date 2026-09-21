using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// <see cref="EfUnitOfWork"/>'s
/// <c>TransientRetryExhausted</c> Warning (EventId <c>6008</c>).
/// </summary>
/// <remarks>
/// Tagged into the <c>"RetryDiagnostics"</c> xUnit collection so the retry-forcing fixtures never run concurrently.
/// </remarks>
[Collection("RetryDiagnostics")]
public sealed class RetryExhaustionLoggingTests
{
    private static (RetryDiagListenerTestDbContext Context, FaultInjectingInterceptor Fault) CreateExhaustingContext(int? configuredMaxRetryCount = null)
    {
        // Always fails — genuine retry-limit exhaustion after AlwaysRetryStrategy's maxRetryCount.
        var faultInjector = new FaultInjectingInterceptor(failuresBeforeSuccess: int.MaxValue);

        var optionsBuilder = new DbContextOptionsBuilder<RetryDiagListenerTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            // Each context here calls ReplaceService/AddInterceptors with fresh instances,
            // which forces EF to build a new internal service provider per context. Past 20
            // EF escalates ManyServiceProvidersCreatedWarning to an exception, which fails
            // these tests only when the full suite runs (CI), never in isolation. The extra
            // providers are intentional test isolation, so the warning is suppressed here.
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .ReplaceService<IExecutionStrategyFactory, AlwaysRetryStrategyFactory>()
                .AddInterceptors(faultInjector);

        // What UsePostgreSQL records for retry-exhaustion logging (the configured retry count).
        if (configuredMaxRetryCount is not null)
        {
            ((Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
                new SharedKernel.Persistence.EfCore.Conventions.PostgreSQLConventionsOptionsExtension(useVector: false, maxRetryCount: configuredMaxRetryCount));
        }

        var options = optionsBuilder.Options;

        var userContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit = PersistenceContextDependencies.Create(userContext, clock);

        var ctx = new RetryDiagListenerTestDbContext(options, audit);
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
        var uow = SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork.For(ctx, dispatcher: null, logger: inMemoryLogger);

        // Act
        Func<Task> act = () => uow.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<RetryLimitExceededException>();

        var record = inMemoryLogger.Records.ShouldHaveLogged(new EventId(6008), LogLevel.Warning);
        record.TryGetProperty("AttemptCount", out var attemptCount).Should().BeTrue();
        attemptCount.Should().Be(1); // no UsePostgreSQL retry configuration on the context — documented fallback
    }

    [Fact]
    public async Task EfUnitOfWork_SaveChangesAsync_RetryExhausted_LogsWarning_WithConfiguredAttemptCount()
    {
        // Arrange
        var (ctx, _) = CreateExhaustingContext(configuredMaxRetryCount: 5);
        await using var _1 = ctx;
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

        var inMemoryLogger = new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var uow = SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork.For(ctx, dispatcher: null, logger: inMemoryLogger);

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
        var audit = PersistenceContextDependencies.Create(userContext, clock);

        await using var ctx = new RetryDiagListenerTestDbContext(options, audit);
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();
        ctx.Items.Add(new RetryDiagListenerTestItem { Name = "x" });

        var inMemoryLogger = new InMemoryLogger<SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork>();
        var uow = SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork.For(ctx, dispatcher: null, logger: inMemoryLogger);

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
        var tuow = SharedKernel.Persistence.EfCore.UnitOfWork.EfUnitOfWork.For(ctx, dispatcher: null, logger: inMemoryLogger);

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
