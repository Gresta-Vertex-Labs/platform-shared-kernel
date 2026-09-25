using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Transactions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.Testing.Tests;

public sealed class Ticket : TenantedAggregateRoot<Guid>
{
    public Ticket(Guid id, Guid tenantId, string title) : base(id, tenantId, new SystemClock()) => Title = title;

    private Ticket() { }

    public string Title { get; private set; } = string.Empty;
}

public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
}

/// <summary>One container for the whole class, started and removed with it — the pattern the README shows.</summary>
public sealed class PostgresServerFixture : IAsyncLifetime
{
    public PostgresTestServer Server { get; private set; } = null!;

    public async Task InitializeAsync() => Server = await PostgresTestServer.StartAsync();

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

public sealed class PostgresTestDatabaseTests(PostgresServerFixture fixture) : IClassFixture<PostgresServerFixture>
{
    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TheCanonicalRoles_AreProvisioned_WithTheirPrivileges()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();

        const string attributes = "SELECT rolsuper::text || '/' || rolbypassrls::text FROM pg_roles WHERE rolname = current_user";
        (await ScalarAsync<string>(database.RuntimeConnectionString, attributes)).Should().Be("false/false");
        (await ScalarAsync<string>(database.MigratorConnectionString, attributes)).Should().Be("false/false");
        (await ScalarAsync<string>(database.CrossTenantConnectionString, attributes)).Should().Be("false/true");
        (await ScalarAsync<string>(database.AuditSealerConnectionString, attributes)).Should().Be("false/false");
        (await ScalarAsync<string>(database.AdminConnectionString, "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = current_database()"))
            .Should().Be(PostgresTestRoles.Migrator);

        Func<Task> createAsRuntime = () => ScalarAsync<int>(database.RuntimeConnectionString, "CREATE TABLE runtime_owned (id int); SELECT 1");
        await createAsRuntime.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege,
            "the application role owns nothing and cannot create tables");
    }

    [Fact]
    public async Task AddSharedKernelPostgres_OnTheGeneratedConfiguration_IsolatesTenants_InTheApplicationAndInTheDatabase()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<TicketDbContext>(database.BuildConfiguration("tickets"), "tickets", p => p.UseMultiTenancy(rowLevelSecurity: true));
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(tenantA));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            await database.CreateSchemaAsync(context);
            await database.EnableRowLevelSecurityAsync(context);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Ticket, Guid>>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .ExecuteInTransactionAsync(ct => repository.AddAsync(new Ticket(Guid.NewGuid(), tenantA, "a"), ct));
        }

        caller.TenantId = tenantB;
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<TicketDbContext>().Tickets.CountAsync()).Should().Be(0);

        caller.TenantId = tenantA;
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<TicketDbContext>().Tickets.CountAsync()).Should().Be(1);

        (await ScalarAsync<long>(database.RuntimeConnectionString, "SELECT count(*) FROM tickets"))
            .Should().Be(0, "row-level security hides every row from the application role when no tenant is bound");
        (await ScalarAsync<long>(database.CrossTenantConnectionString, "SELECT count(*) FROM tickets"))
            .Should().Be(1, "the cross-tenant role bypasses row-level security");
    }

    [Fact]
    public async Task TheAuditLedger_AndTheTenantKeyTable_GetTheGrantsOfTheRoleScript()
    {
        await using var database = await fixture.Server.CreateDatabaseAsync();

        await database.CreateAuditLedgerAsync(separateAuditSealer: true);
        await database.CreateTenantEncryptionKeyTableAsync();

        await FluentActions.Awaiting(() => ScalarAsync<long>(database.RuntimeConnectionString, "UPDATE audit_records SET action = action; SELECT 1::bigint"))
            .Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        await FluentActions.Awaiting(() => ScalarAsync<long>(database.RuntimeConnectionString, "INSERT INTO audit_chain_links DEFAULT VALUES; SELECT 1::bigint"))
            .Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege,
                "with a separate sealer only the sealer appends links");
        await FluentActions.Awaiting(() => ScalarAsync<long>(database.RuntimeConnectionString, "DELETE FROM sk_tenant_encryption_keys; SELECT 1::bigint"))
            .Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        (await ScalarAsync<long>(database.RuntimeConnectionString, "SELECT count(*) FROM audit_records")).Should().Be(0);
    }

    [Fact]
    public async Task ConfigurationFor_NamesEveryRole_AndDisposeDropsTheDatabase()
    {
        var database = await fixture.Server.CreateDatabaseAsync("sk_named_database");

        var configuration = database.ConfigurationFor("orders", separateAuditSealer: true);
        configuration["ConnectionStrings:orders"].Should().Contain($"Username={PostgresTestRoles.Runtime}");
        configuration["SharedKernel:Persistence:orders:MigrationConnectionString"].Should().Contain($"Username={PostgresTestRoles.Migrator}");
        configuration["SharedKernel:Persistence:orders:RowLevelSecurity:CrossTenantConnectionString"].Should().Contain($"Username={PostgresTestRoles.CrossTenant}");
        configuration["SharedKernel:Persistence:Auditing:Sealer:DataSourceName"].Should().Be(PostgresTestDatabase.AuditSealerDataSourceName);
        configuration["SharedKernel:Persistence:audit-sealer:ConnectionString"].Should().Contain($"Username={PostgresTestRoles.AuditSealer}");

        await database.DisposeAsync();

        (await ScalarAsync<long>(fixture.Server.AdminConnectionString, "SELECT count(*) FROM pg_database WHERE datname = 'sk_named_database'"))
            .Should().Be(0);
        await FluentActions.Awaiting(() => fixture.Server.CreateDatabaseAsync("Not-Valid")).Should().ThrowAsync<ArgumentException>();
    }
}
