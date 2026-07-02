using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using AppBehaviorsIUnitOfWork = SharedKernel.Application.Behaviors.Transaction.IUnitOfWork;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>
/// T-59: EfUnitOfWork dual-interface resolution tests (P-228).
/// T-60: TransactionBehavior end-to-end consumer-verify test (P-228).
/// </summary>
public sealed class EfUnitOfWorkDualInterfaceTests
{
    // ===========================================================================
    // T-59: EfUnitOfWork dual-interface resolution tests
    // ===========================================================================

    [Fact]
    public void T59_EfUnitOfWork_Implements_Both_IUnitOfWork_Interfaces()
    {
        // EfUnitOfWork must declare both IUnitOfWork interfaces.
        typeof(EfUnitOfWork).Should().Implement<SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork>(
            "EfUnitOfWork must implement Persistence.Abstractions.IUnitOfWork");

        typeof(EfUnitOfWork).Should().Implement<AppBehaviorsIUnitOfWork>(
            "EfUnitOfWork must implement Application.Behaviors.Transaction.IUnitOfWork (P-228)");
    }

    [Fact]
    public void T59_WithApplicationTransactionBehavior_Both_IUnitOfWork_Types_Resolve_Same_Instance()
    {
        // After WithApplicationTransactionBehavior(), both IUnitOfWork interfaces must
        // resolve the SAME scoped EfUnitOfWork instance (not two independent instances).
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithApplicationTransactionBehavior()
            .Build();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var persistenceUow = scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.Abstractions.UnitOfWork.IUnitOfWork>();
        var appBehaviorsUow = scope.ServiceProvider.GetRequiredService<AppBehaviorsIUnitOfWork>();

        persistenceUow.Should().BeSameAs(appBehaviorsUow,
            "both IUnitOfWork registrations must resolve the SAME scoped EfUnitOfWork instance per DI scope (P-228)");
    }

    [Fact]
    public void T59_Without_WithApplicationTransactionBehavior_AppBehaviors_IUnitOfWork_NotRegistered()
    {
        // Omitting WithApplicationTransactionBehavior must leave Application.Behaviors.IUnitOfWork unresolvable.
        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite("DataSource=:memory:")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .Build();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var descriptor = services.FirstOrDefault(sd =>
            sd.ServiceType == typeof(AppBehaviorsIUnitOfWork));

        descriptor.Should().BeNull(
            "Application.Behaviors.Transaction.IUnitOfWork must NOT be registered when " +
            ".WithApplicationTransactionBehavior() is not called (P-228, opt-in only)");
    }

    [Fact]
    public async Task T59_SaveChangesAsync_Via_Either_Interface_Fires_Same_Interceptors_Once()
    {
        // SaveChangesAsync via the App.Behaviors interface must commit to the database exactly once
        // (same interceptor chain, same underlying context).
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite(connection)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithApplicationTransactionBehavior()
            .Build();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var appUow = scope.ServiceProvider.GetRequiredService<AppBehaviorsIUnitOfWork>();

        // Add an entity and save via the Application.Behaviors IUnitOfWork
        var id = TestId.New();
        var entityClock = new SystemClock();
        dbContext.TestAggregates.Add(new TestAggregate(id, "test-p228", entityClock));
        var count = await appUow.SaveChangesAsync(CancellationToken.None);

        count.Should().Be(1, "SaveChangesAsync via Application.Behaviors.IUnitOfWork must persist one row");

        // Verify via fresh context
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        await using var freshCtx = new TestDbContext(
            dbOptions,
            new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor());

        var found = await freshCtx.TestAggregates.FindAsync(id);
        found.Should().NotBeNull("entity must be persisted after saving via Application.Behaviors.IUnitOfWork");

        connection.Close();
    }

    // ===========================================================================
    // T-60: TransactionBehavior end-to-end consumer-verify test (P-228)
    // ===========================================================================

    /// <summary>
    /// Minimal test command that adds an entity to the DbContext (via a captured reference).
    /// </summary>
    private sealed record TestPersistCommand(TestId Id, string Name) : ICommand;

    private sealed class TestPersistCommandHandler(TestDbContext dbContext, IClock clock)
        : ICommandHandler<TestPersistCommand>
    {
        public Task<Result> Handle(TestPersistCommand request, CancellationToken cancellationToken)
        {
            dbContext.TestAggregates.Add(new TestAggregate(request.Id, request.Name, clock));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<TestPersistCommand>
    {
        public Task<Result> Handle(TestPersistCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler threw — no commit should happen");
    }

    [Fact]
    public async Task T60_TransactionBehavior_WithApplicationTransactionBehavior_PersistesMutationAfterHandler()
    {
        // Full MediatR pipeline test:
        //   1. Configure EF Core with WithApplicationTransactionBehavior()
        //   2. Register AddTransactionBehavior() from Application.Behaviors
        //   3. Dispatch a test command
        //   4. Verify the mutation is persisted (SaveChangesAsync called exactly once by TransactionBehavior)
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        // Register EF Core with the Application.Behaviors bridge
        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite(connection)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithApplicationTransactionBehavior()
            .Build();

        // Register MediatR with the handler + TransactionBehavior
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(EfUnitOfWorkDualInterfaceTests).Assembly));
        services.AddSharedKernelApplicationBehaviors()
            .AddTransactionBehavior()
            .Build();

        // Register a scoped ICommandHandler that uses the DbContext
        services.AddScoped<IRequestHandler<TestPersistCommand, Result>, TestPersistCommandHandler>();
        services.AddScoped<TestPersistCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var id = TestId.New();

        // Act: dispatch the command through the full MediatR pipeline (TransactionBehavior commits)
        var result = await sender.Send(new TestPersistCommand(id, "TransactionBehaviorTest"));

        // Assert: command succeeded
        result.IsSuccess.Should().BeTrue("command handler must return success");

        // Assert: entity persisted — verify via fresh context on the same connection
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        await using var freshCtx = new TestDbContext(
            dbOptions,
            new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor());

        var found = await freshCtx.TestAggregates.FindAsync(id);
        found.Should().NotBeNull(
            "entity must be persisted after TransactionBehavior calls SaveChangesAsync via Application.Behaviors.IUnitOfWork");

        connection.Close();
    }

    [Fact]
    public async Task T60_TransactionBehavior_ThrowingHandler_NoPartialCommit()
    {
        // If the handler throws, TransactionBehavior must NOT call SaveChangesAsync
        // (no partial commit — the mutation staged in the handler is not persisted).
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();

        services
            .AddSharedKernelEfCore<TestDbContext>(opts =>
                opts.UseSqlite(connection)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithApplicationTransactionBehavior()
            .Build();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(EfUnitOfWorkDualInterfaceTests).Assembly));
        services.AddSharedKernelApplicationBehaviors()
            .AddTransactionBehavior()
            .Build();

        // Register the THROWING handler
        services.AddScoped<IRequestHandler<TestPersistCommand, Result>, ThrowingCommandHandler>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var id = TestId.New();

        // Act: handler throws → TransactionBehavior must not reach SaveChangesAsync
        var act = async () => await sender.Send(new TestPersistCommand(id, "ShouldNotPersist"));
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert: entity was NOT persisted
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        await using var freshCtx = new TestDbContext(
            dbOptions,
            new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor());

        var found = await freshCtx.TestAggregates.FindAsync(id);
        found.Should().BeNull(
            "entity must NOT be persisted when the handler throws (TransactionBehavior must not commit)");

        connection.Close();
    }
}
