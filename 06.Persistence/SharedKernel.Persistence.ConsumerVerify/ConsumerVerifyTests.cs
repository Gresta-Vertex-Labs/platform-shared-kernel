using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.EfCore;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;
using Testcontainers.PostgreSql;
using Xunit;

namespace SharedKernel.Persistence.ConsumerVerify;

// ---------------------------------------------------------------------------
// A consumer's domain and context, written exactly as the packages document: no configuration base class, no
// per-type strongly-typed id registration, no concurrency property — all conventions.
// ---------------------------------------------------------------------------

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}

public sealed class Order : AuditableAggregateRoot<OrderId>, IHasTenant
{
    public Order(OrderId id, Guid tenantId, string customerEmail, decimal total, IClock clock)
        : base(id, clock)
    {
        TenantId = tenantId;
        CustomerEmail = customerEmail;
        Total = total;
    }

    private Order() { } // ORM materialization path

    public Guid TenantId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public decimal Total { get; set; }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.CustomerEmail).IsRequired().Encrypt("orders.customer_email");
    }
}

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Order> Orders => Set<Order>();
}

public sealed class ConsumerVerifyTests
{
    private const string UnusedConnectionString = "Host=localhost;Port=1;Database=consumer_verify;Username=x;Password=x";

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:orders"] = connectionString,
                ["SharedKernel:Persistence:Auditing:CurrentKeyId"] = "k1",
                ["SharedKernel:Persistence:Auditing:Keys:k1:Material"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()),
                ["SharedKernel:Persistence:Auditing:Keys:k1:Order"] = "1",
            })
            .Build();

    private static void AddEncryptionKeys(IServiceCollection services)
    {
        // ONE key-provider registration; UseFieldEncryption() finds it.
        var key = new CryptographicKey("key-1", Enumerable.Repeat((byte)9, 32).ToArray());
        services.AddSingleton<IEncryptionKeyProvider>(new StaticEncryptionKeyProvider("key-1", [key]));
    }

    // -----------------------------------------------------------------------
    // Dependency graph of the packed assemblies.
    // -----------------------------------------------------------------------

    [Fact]
    public void EfCorePackage_IsThePostgreSqlProvider()
    {
        var references = typeof(SharedKernelDbContext).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", references);
        Assert.Contains("SharedKernel.Persistence.Npgsql", references);
    }

    [Fact]
    public void DapperPackage_NeverReferencesEntityFrameworkCore()
    {
        var references = typeof(IDbSessionFactory).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("SharedKernel.Persistence.EfCore", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractionsPackage_NeverReferencesAnOrm()
    {
        var references = typeof(IDbConnectionFactory).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal)
            || name == "Dapper");
    }

    // -----------------------------------------------------------------------
    // The one-line entry point wires everything without opening a connection.
    // -----------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelPostgres_WiresTheWholeStack_WithoutConnecting()
    {
        var configuration = Configuration(UnusedConnectionString);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCryptography(configuration);
        AddEncryptionKeys(services);

        services.AddSharedKernelPostgres<OrdersDbContext>(configuration, "orders", p => p
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseAuditTrail()
            .UseFieldEncryption());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<OrdersDbContext>());
        Assert.IsAssignableFrom<IUnitOfWork<OrdersDbContext>>(sp.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(sp.GetRequiredService<IRepository<Order, OrderId>>());
        Assert.NotNull(sp.GetRequiredService<IReadRepository<Order, OrderId>>());
        Assert.NotNull(sp.GetRequiredService<ICrossTenantScope>());
        Assert.NotNull(sp.GetRequiredService<IAuditTrailWriter>());
        Assert.NotNull(sp.GetRequiredService<IAuditQueryService>());
        Assert.NotNull(sp.GetRequiredService<IDbConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<NpgsqlDataSource>());
        Assert.NotNull(provider.GetRequiredService<ICallerDbContextFactory<OrdersDbContext>>());
    }

    [Fact]
    public void AddSharedKernelDapper_ForADapperOnlyService_RegistersTheSessionFactoryAndCrossTenantScope()
    {
        var configuration = Configuration(UnusedConnectionString);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelNpgsql(configuration, "orders");
        services.AddSharedKernelDapper();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDbSessionFactory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICrossTenantScope>());
    }

    [Fact]
    public void StartupValidation_MissingConnectionString_FailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var empty = new ConfigurationBuilder().Build();
        services.AddSharedKernelPostgres<OrdersDbContext>(empty, "orders");

        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("ConnectionStrings:orders", failure.Message);
    }

    // -----------------------------------------------------------------------
    // Composed scenario against a real PostgreSQL: multi-tenancy + RLS + audit trail + encryption + retry.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Composed_MultiTenancy_RowLevelSecurity_AuditTrail_Encryption_Retry()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16.4").Build();
        await postgres.StartAsync();

        // The application role must not own the tables and must not bypass RLS (the startup self-check enforces it).
        var admin = postgres.GetConnectionString();
        await using (var setup = NpgsqlDataSource.Create(admin))
        {
            await using var command = setup.CreateCommand(
                "CREATE ROLE app LOGIN PASSWORD 'app' NOSUPERUSER NOBYPASSRLS; GRANT USAGE ON SCHEMA public TO app;");
            await command.ExecuteNonQueryAsync();
        }

        var configuration = Configuration(new NpgsqlConnectionStringBuilder(admin) { Username = "app", Password = "app" }.ConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        ServiceProvider Services(Guid? tenant)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSharedKernelCryptography(configuration);
            AddEncryptionKeys(services);
            services.AddSingleton<IRequestContext>(new SystemRequestContext([], "consumer-verify", tenant));
            services.AddSharedKernelPostgres<OrdersDbContext>(configuration, "orders", p => p
                .UseMultiTenancy(rowLevelSecurity: true)
                .UseAuditTrail()
                .UseFieldEncryption());
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        }

        // Schema: created by the admin (owner) the way migrations would — the tables, the tenant policy
        // (EnableTenantRowLevelSecurity) and the audit ledger — then the least-privilege grants from the READMEs.
        var adminServices = new ServiceCollection();
        adminServices.AddLogging();
        AddEncryptionKeys(adminServices);
        adminServices.AddSharedKernelPostgres<OrdersDbContext>(Configuration(admin), "orders", p => p.UseFieldEncryption());
        await using (var adminProvider = adminServices.BuildServiceProvider())
        await using (var scope = adminProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.Database.EnsureCreatedAsync();

            var migration = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder(activeProvider: "Npgsql");
            migration.EnableTenantRowLevelSecurity("orders");
            foreach (var operation in migration.Operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
                await db.Database.ExecuteSqlRawAsync(operation.Sql);

            await db.Database.ExecuteSqlRawAsync(AuditLedgerSchema.CreateScript);
            await db.Database.ExecuteSqlRawAsync(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON orders TO app; " +
                "GRANT SELECT, INSERT ON audit_records, audit_chain_links, audit_checkpoints TO app; " +
                "GRANT SELECT, INSERT, DELETE ON audit_record_payloads TO app;");
        }

        await using var servicesA = Services(tenantA);
        var id = OrderId.New();

        // Write: one transaction (retry-safe), audit record committed with it, email encrypted at rest.
        await using (var scope = servicesA.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
            {
                await sp.GetRequiredService<IRepository<Order, OrderId>>()
                    .AddAsync(new Order(id, tenantA, "ada@example.com", 42m, new SystemClock()), ct);
                await sp.GetRequiredService<IAuditTrailWriter>().RecordAsync(new AuditEntry
                {
                    Action = "order.created",
                    ResourceType = nameof(Order),
                    ResourceId = id.Value.ToString(),
                    Outcome = AuditOutcome.Succeeded,
                }, ct);
            });
        }

        await using (var raw = NpgsqlDataSource.Create(admin))
        await using (var command = raw.CreateCommand("SELECT customer_email FROM orders"))
            Assert.DoesNotContain("ada@example.com", (string)(await command.ExecuteScalarAsync())!);

        // Read back decrypted, with its version (ETag): an opaque token, sealed with a subkey of the registered key
        // provider. The current version is accepted; the same version once someone else changed the order is a conflict
        // carrying the current one.
        EntityVersion version;
        await using (var scope = servicesA.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, OrderId>>();
            var order = (await repository.GetByIdAsync(id))!;
            Assert.Equal("ada@example.com", order.CustomerEmail);
            version = ConcurrencyVersion.Get(scope.ServiceProvider.GetRequiredService<OrdersDbContext>(), order);
            Assert.Equal(28, version.ToString().Length);

            order.Total = 50m;
            await repository.UpdateAsync(order, EntityVersion.Parse($"\"{version}\"")); // the client's If-Match
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = servicesA.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, OrderId>>();
            var order = (await repository.GetByIdAsync(id))!;
            order.Total = 60m;
            await repository.UpdateAsync(order, EntityVersion.Parse($"W/\"{version}\"")); // a stale If-Match header
            var conflict = await Assert.ThrowsAsync<ConflictException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
            Assert.True(ConcurrencyVersion.TryGetCurrentVersion(conflict, out var current));
            Assert.NotEqual(version, current);
        }

        // Tenant B sees nothing — neither through the EF filter nor through the database policy.
        await using var servicesB = Services(tenantB);
        await using (var scope = servicesB.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IReadRepository<Order, OrderId>>().GetByIdAsync(id));

            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            Assert.Equal(0, await db.Orders.IgnoreQueryFilters([SharedKernel.Persistence.EfCore.Context.PersistenceFilterNames.Tenant]).CountAsync());
        }

        // The audit trail recorded the write in tenant A's chain.
        await using (var scope = servicesA.CreateAsyncScope())
        {
            var history = await scope.ServiceProvider.GetRequiredService<IAuditQueryService>()
                .QueryAsync(new AuditRecordQuery { ResourceType = nameof(Order), ResourceId = id.Value.ToString() });
            Assert.Single(history.Items);
        }
    }
}
