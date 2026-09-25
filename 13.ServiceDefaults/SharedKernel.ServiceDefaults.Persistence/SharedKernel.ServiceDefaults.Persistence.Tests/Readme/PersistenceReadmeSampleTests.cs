using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Execution.Tenancy;
using FluentAssertions;
using SharedKernel.Application.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Execution.Transactions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.Readme;

// ---------------------------------------------------------------------------------------------------------
// The main sample of 06.Persistence/README.md ("A multi-tenant service in 10 minutes"), compiled and run
// against the real API. Keep the two in step: a README edit that changes a type, a call or a namespace
// changes this file too. Differences, on purpose: authentication (AddOidcAuthentication +
// AddSharedKernelRequestContext) is replaced by AddTestRequestContext, and the schema comes from
// PostgresTestDatabase instead of MigrateOnStartup (a bare ServiceProvider runs no hosted services).
// ---------------------------------------------------------------------------------------------------------

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}

public sealed class Order : TenantedAuditableAggregateRoot<OrderId>
{
    public Order(OrderId id, TenantId tenantId, string customer, Money total, IClock clock)
        : base(id, tenantId, clock)
    {
        Customer = customer;
        Total = total;
    }

    private Order() { } // EF Core materialization

    public string Customer { get; private set; } = string.Empty;

    public Money Total { get; private set; } = null!;
}

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Order> Orders => Set<Order>();

    // Not in the README: this test assembly holds another context's IEntityTypeConfiguration, and a context
    // applies every configuration of its own assembly unless told otherwise (see "Several contexts").
    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

// README step 5: the design-time factory, with the same capabilities as the registration.
public sealed class OrderDbContextFactory() : PostgresDesignTimeDbContextFactory<OrderDbContext>("orders")
{
    protected override OrderDbContext Create(DbContextOptions<OrderDbContext> options, PersistenceContextDependencies dependencies)
        => new(options, dependencies);

    protected override void ConfigurePersistence(EfCorePersistenceBuilder<OrderDbContext> persistence)
        => persistence.UseMultiTenancy(rowLevelSecurity: true).UseAuditTrail();
}

public sealed record PlaceOrder(OrderId Id, string Customer, decimal Amount)
    : ICommand<OrderId>, IAuditableRequest<Result<OrderId>>
{
    public string Action => "order.placed";
    public string ResourceType => nameof(Order);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result<OrderId> response) => null;
}

public sealed class PlaceOrderHandler(
    IRepository<Order, OrderId> orders,
    IRequestContext caller,
    IClock clock)
    : ICommandHandler<PlaceOrder, OrderId>
{
    public async Task<Result<OrderId>> Handle(PlaceOrder command, CancellationToken cancellationToken)
    {
        var total = Money.Create(command.Amount, Currency.Eur).Value;
        await orders.AddAsync(new Order(command.Id, caller.TenantId!.Value, command.Customer, total, clock), cancellationToken);
        return command.Id; // no SaveChanges: TransactionBehavior saves and commits
    }
}

[Collection("AuditWiringPostgres")]
public sealed class PersistenceReadmeSampleTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Dictionary<string, string?> AuditKeys = new()
    {
        ["SharedKernel:Persistence:Auditing:CurrentKeyId"] = "k1",
        ["SharedKernel:Persistence:Auditing:Keys:k1:Material"] = Convert.ToBase64String(Enumerable.Repeat((byte)0x31, 32).ToArray()),
        ["SharedKernel:Persistence:Auditing:Keys:k1:Order"] = "1",
    };

    /// <summary>The README's registration, as a service collection (the README uses the host builder overload).</summary>
    private static ServiceProvider BuildServices(IConfiguration configuration, TestRequestContext caller)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);

        services.AddTestRequestContext(caller);                        // README: AddSharedKernelRequestContext()
        services.AddSharedKernelCryptography(configuration);           // the audit ledger's MAC
        services.AddSharedKernelPostgres<OrderDbContext>(configuration, "orders", p => p
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseAuditTrail()
            // Test harness only: every test builds its own model.
            .ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))));

        services.AddSharedKernelMediatR(typeof(PersistenceReadmeSampleTests).Assembly); // handlers + domain-event dispatcher
        services.AddSharedKernelApplicationBehaviors()
            .AddDefaultBehaviors()
            .AddTransactionBehavior()
            .AddAuditingBehavior()
            .Build();

        services.AddHealthChecks()
            .AddDatabaseReadinessCheck<OrderDbContext>()
            .AddPersistenceStartupReadinessCheck()
            .AddSharedKernelReadiness();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task TenMinuteSample_PlacesAnOrderThroughMediatR_IsolatedByTenant_AndAudited()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();
        var configuration = database.BuildConfiguration("orders", AuditKeys);
        var tenantA = new TenantId(Guid.NewGuid());
        var caller = TestRequestContext.ForTenant(tenantA);

        await using var provider = BuildServices(configuration, caller);

        // Schema as the migration role: tables, RLS policies for the whole model, the audit ledger.
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            await database.CreateSchemaAsync(context);
            await database.EnableRowLevelSecurityAsync(context);
            await database.CreateAuditLedgerAsync();
        }

        var id = OrderId.New();
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PlaceOrder(id, "Ada", 42m));
            result.IsSuccess.Should().BeTrue();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var order = await scope.ServiceProvider.GetRequiredService<IReadRepository<Order, OrderId>>().GetByIdAsync(id);
            order.Should().NotBeNull();
            order!.Total.Should().Be(Money.Create(42m, Currency.Eur).Value);

            var audit = await scope.ServiceProvider.GetRequiredService<IAuditQueryService>()
                .QueryAsync(new AuditRecordQuery { ResourceType = nameof(Order), ResourceId = id.Value.ToString() });
            audit.Items.Should().ContainSingle();
        }

        caller.TenantId = new TenantId(Guid.NewGuid()); // another tenant: neither the EF filter nor the RLS policy shows the row
        await using (var scope = provider.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IReadRepository<Order, OrderId>>().GetByIdAsync(id))
                .Should().BeNull();
        }
    }

    [Fact]
    public void DesignTimeFactorySample_BuildsTheModel_OnTheMigrationConnection()
    {
        using var context = new OrderDbContextFactory().CreateDbContext(["--connection", "Host=localhost;Port=1;Database=orders;Username=app_migrator"]);

        context.Model.FindEntityType(typeof(Order)).Should().NotBeNull();
        context.Database.GetConnectionString().Should().Contain("app_migrator");
    }

    [Fact]
    public async Task UnitTestSample_RunsTheHandlerOverTheFakes()
    {
        var services = new ServiceCollection();
        var orders = services.AddFakeRepository<Order, OrderId>();
        var unitOfWork = services.AddFakeUnitOfWork();
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(new TenantId(Guid.NewGuid())));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<PlaceOrderHandler>();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<PlaceOrderHandler>();
        var id = OrderId.New();
        var result = await provider.GetRequiredService<IUnitOfWork>()
            .ExecuteInTransactionAsync(ct => handler.Handle(new PlaceOrder(id, "Ada", 42m), ct));

        result.IsSuccess.Should().BeTrue();
        unitOfWork.CommitCount.Should().Be(1);
        orders.Items.Should().ContainKey(id);
        orders.Items[id].TenantId.Should().Be(caller.TenantId!.Value);
    }
}
