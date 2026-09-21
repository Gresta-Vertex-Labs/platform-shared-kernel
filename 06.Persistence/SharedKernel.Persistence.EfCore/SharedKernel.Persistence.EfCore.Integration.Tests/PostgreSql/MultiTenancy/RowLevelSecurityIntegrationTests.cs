using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.MultiTenancy;

/// <summary>
/// Row-level security end to end against a real PostgreSQL, connecting as a genuinely unprivileged role
/// (a superuser bypasses RLS even under FORCE): transaction-local binding for plain queries and unit-of-work
/// transactions, no binding surviving on a reused physical connection (the PgBouncer transaction-mode
/// hazard, A1), the single-predicate policy (A2) using the tenant index, the cross-tenant role, and the
/// Down migration switching RLS fully off (A3).
/// </summary>
public sealed class RowLevelSecurityIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "sk_rls_ef";
    private const string AppRole = "rls_ef_app";
    private const string BypassRole = "rls_ef_bypass";
    private const string PolicyRole = "rls_ef_policy_role";
    private const string Password = "rls_ef_pw";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly PostgreSqlContainerFixture _fixture = new();
    private string _adminConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _adminConnectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

        var adminOptions = new DbContextOptionsBuilder<RlsTestDbContext>();
        adminOptions.UsePostgreSQL(TestNpgsqlDataSources.Get(_adminConnectionString), o => o.Retry.Enabled = false);
        await using (var adminContext = new RlsTestDbContext(adminOptions.Options, Dependencies()))
            await adminContext.Database.EnsureCreatedAsync();

        await AdminExecuteAsync($"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{Password}';
            CREATE ROLE {BypassRole} LOGIN BYPASSRLS PASSWORD '{Password}';
            CREATE ROLE {PolicyRole} LOGIN PASSWORD '{Password}';
            GRANT SELECT, INSERT, UPDATE, DELETE ON rls_order TO {AppRole}, {BypassRole}, {PolicyRole};
            """);

        var migration = new MigrationBuilder(activeProvider: "Npgsql");
        migration.EnableTenantRowLevelSecurity("rls_order", crossTenantRole: PolicyRole);
        foreach (var operation in migration.Operations.OfType<SqlOperation>())
            await AdminExecuteAsync(operation.Sql);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    // ---------------------------------------------------------------- plain queries and writes

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task Read_OutsideATransaction_SeesOnlyTheBoundTenant_EvenIgnoringEfsFilter(bool tenantA, int expected)
    {
        await SeedAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = tenantA ? TenantA : TenantB;

        using var scope = host.Provider.CreateScope();
        var rows = await Orders(scope).IgnoreQueryFilters().ToListAsync();

        rows.Should().HaveCount(expected).And.OnlyContain(r => r.TenantId == host.Tenant.TenantId);
    }

    [Fact]
    public async Task Read_NoTenantBound_SeesNothing()
    {
        await SeedAsync((TenantA, "A-1"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString());

        using var scope = host.Provider.CreateScope();
        (await Orders(scope).IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Write_NoTenantBound_IsRejectedByTheApplicationGuard_BeforeReachingTheDatabase()
    {
        // Defense in depth: the tenant write guard is always on for a TenantedDbContext, so the application
        // rejects the write before any SQL is sent; the database policy is the second line (next test).
        await SeedAsync();
        using var host = BuildHost(AppConnectionString());

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "rejected" });

        var act = () => context.SaveChangesAsync();

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.InnerException.Should().BeNull("the application guard fires before any SQL, so there is no DbUpdateException");
        host.Commands.Should().NotContain(c => c.Contains("INSERT", StringComparison.Ordinal));
        (await AdminCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Write_BypassingTheApplicationGuard_IsRejectedByTheDatabasePolicy()
    {
        // Raw SQL skips every EF Core save interceptor, so only the row-level security policy stands between
        // the statement and the table.
        await SeedAsync();
        using var host = BuildHost(AppConnectionString());

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        var noTenant = () => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO rls_order (id, tenant_id, description) VALUES ({Guid.NewGuid()}, {TenantA}, 'raw')");
        (await noTenant.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("row-level security");

        host.Tenant.TenantId = TenantA;
        var otherTenant = () => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO rls_order (id, tenant_id, description) VALUES ({Guid.NewGuid()}, {TenantB}, 'raw')");
        (await otherTenant.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("row-level security");

        (await AdminCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Write_ForAnotherTenant_IsRejected_ButForTheBoundTenant_Succeeds()
    {
        await SeedAsync();
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantB, Description = "cross-tenant" });
        await context.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<ForbiddenException>();

        context.ChangeTracker.Clear();
        context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "own" });
        await context.SaveChangesAsync();

        (await AdminCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveChanges_OutsideAUnitOfWork_BatchOfWrites_KeepsEfsPerStatementRowCounts()
    {
        await SeedAsync();
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var orders = Enumerable.Range(1, 3)
            .Select(i => new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = $"A-{i}" })
            .ToList();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        orders[0].Description = "A-1 changed";
        context.Orders.Remove(orders[1]);
        await context.SaveChangesAsync();

        (await AdminCountAsync()).Should().Be(2);
        host.Commands.Should().NotContain(
            c => c.Contains("$sk_rls$", StringComparison.Ordinal) && c.Contains("INSERT", StringComparison.Ordinal),
            "a SaveChanges batch is bound by a separate command, never by a prefix that would shift its statement results");
    }

    [Fact]
    public async Task UpdateMovingARowToAnotherTenant_IsRejected_ByWithCheck()
    {
        var id = Guid.NewGuid();
        await SeedAsync(id, (TenantA, "A-1"));
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        var act = () => context.Database.ExecuteSqlRawAsync(
            "UPDATE rls_order SET tenant_id = {0} WHERE id = {1}", TenantB, id);

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    // ---------------------------------------------------------------- transactions

    [Fact]
    public async Task UnitOfWork_BindsOncePerTransaction_AndEveryCommandSeesTheTenant()
    {
        await SeedAsync((TenantA, "A-1"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        host.Commands.Clear();

        var counts = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var first = await context.Orders.IgnoreQueryFilters().CountAsync(ct);
            context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "A-2" });
            await context.SaveChangesAsync(ct);
            var second = await context.Orders.IgnoreQueryFilters().CountAsync(ct);
            return (first, second);
        });

        counts.Should().Be((1, 2));
        host.Commands.Count(c => c.Contains("$sk_rls$", StringComparison.Ordinal)).Should().Be(
            1, "inside one transaction the tenant is bound by the first command only");
    }

    [Fact]
    public async Task ExplicitTransaction_TenantChange_IsRebound()
    {
        await SeedAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        host.Tenant.TenantId = TenantB;
        (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(1);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RollbackToSavepoint_UndoesTheBinding_AndTheNextCommandBindsAgain()
    {
        await SeedAsync((TenantA, "A-1"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString());
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await transaction.CreateSavepointAsync("before_read");
        (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        await transaction.RollbackToSavepointAsync("before_read");

        (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(
            1, "the savepoint rollback discarded the set_config, so the interceptor must bind again");

        await transaction.RollbackAsync();
    }

    // ---------------------------------------------------------------- PgBouncer-style reuse (A1)

    [Fact]
    public async Task ReusedPhysicalConnection_CarriesNoTenant_IntoTheNextLease()
    {
        await SeedAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        // One physical connection, never reset between leases — what a transaction-mode pooler does.
        var connectionString = new NpgsqlConnectionStringBuilder(AppConnectionString())
        {
            MaxPoolSize = 1,
            NoResetOnClose = true,
        }.ConnectionString;
        using var host = BuildHost(connectionString);
        host.Tenant.TenantId = TenantA;

        int pidA;
        using (var scope = host.Provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(2);
            pidA = await BackendPidAsync(context);

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
                (await context.Orders.IgnoreQueryFilters().CountAsync(ct)).Should().Be(2));
        }

        await using (var raw = await TestNpgsqlDataSources.Get(connectionString).OpenConnectionAsync())
        {
            (await ScalarAsync<int>(raw, "SELECT pg_backend_pid()")).Should().Be(pidA, "the same physical connection is reused");
            (await ScalarAsync<string?>(raw, "SELECT current_setting('app.tenant_id', true)")).Should().BeNullOrEmpty();
            (await ScalarAsync<long>(raw, "SELECT count(*) FROM rls_order")).Should().Be(0, "no tenant leaked into this lease");
        }

        host.Tenant.TenantId = TenantB;
        using (var scope = host.Provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
            var rows = await context.Orders.IgnoreQueryFilters().ToListAsync();
            rows.Should().ContainSingle().Which.TenantId.Should().Be(TenantB);
            (await BackendPidAsync(context)).Should().Be(pidA);
        }
    }

    // ---------------------------------------------------------------- cross-tenant role

    [Theory]
    [InlineData(BypassRole)]
    [InlineData(PolicyRole)]
    public async Task CrossTenant_OnTheCrossTenantRole_SeesAndWritesEveryTenant(string role)
    {
        await SeedAsync((TenantA, "A-1"), (TenantB, "B-1"));
        using var host = BuildHost(AppConnectionString(), crossTenantConnectionString: RoleConnectionString(role));
        host.Tenant.TenantId = TenantA;

        using (host.Scope.Enter("rls-test"))
        {
            using var scope = host.Provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RlsTestDbContext>>().CreateDbContext();
            await using var _ = context;
            context.Database.UseCrossTenantConnection();

            (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(2);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO rls_order (id, tenant_id, description) VALUES ({0}, {1}, 'x')", Guid.NewGuid(), TenantB);
        }

        (await AdminCountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task StartupSeeder_UnderRowLevelSecurity_WithPooling_WritesEveryTenant_OnTheCrossTenantConnection()
    {
        // A tenanted seeder writes across tenants, which the application role's policy forbids; the startup
        // service runs it inside a cross-tenant scope on a dedicated (unpooled) context switched to the
        // cross-tenant role's connection — a pooled context could not keep the switched connection safely.
        await SeedAsync();
        using var host = BuildHost(
            AppConnectionString(),
            crossTenantConnectionString: RoleConnectionString(BypassRole),
            configure: p => p.UseDbContextPooling(poolSize: 8).AddSeeder<TwoTenantSeeder>());

        foreach (var hosted in host.Provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                     .Where(s => s.GetType().Name.StartsWith("MigrationAndSeedHostedService", StringComparison.Ordinal)))
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        (await AdminCountAsync()).Should().Be(2);
    }

    private sealed class TwoTenantSeeder : SharedKernel.Persistence.EfCore.Seeding.IDataSeeder<RlsTestDbContext>
    {
        public async Task SeedAsync(RlsTestDbContext context, CancellationToken cancellationToken)
        {
            context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "seed-A" });
            context.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantB, Description = "seed-B" });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task CrossTenantScope_OnTheApplicationRole_IsRefused()
    {
        await SeedAsync((TenantA, "A-1"));
        using var host = BuildHost(AppConnectionString(), crossTenantConnectionString: RoleConnectionString(BypassRole));
        host.Tenant.TenantId = TenantA;

        using var scope = host.Provider.CreateScope();
        using (host.Scope.Enter("rls-test"))
        {
            var act = () => Orders(scope).IgnoreQueryFilters().CountAsync();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseCrossTenantConnection*");
        }
    }

    [Fact]
    public async Task CrossTenantConnection_AfterTheScopeEnded_IsRefused()
    {
        await SeedAsync((TenantA, "A-1"));
        using var host = BuildHost(AppConnectionString(), crossTenantConnectionString: RoleConnectionString(BypassRole));

        using var scope = host.Provider.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RlsTestDbContext>>().CreateDbContext();
        using (host.Scope.Enter("rls-test"))
            context.Database.UseCrossTenantConnection();

        var act = () => context.Orders.IgnoreQueryFilters().CountAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no cross-tenant scope*");
    }

    [Fact]
    public void UseCrossTenantConnection_OutsideAScope_Throws()
    {
        using var host = BuildHost(AppConnectionString(), crossTenantConnectionString: RoleConnectionString(BypassRole));
        using var scope = host.Provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RlsTestDbContext>>().CreateDbContext();

        var act = () => context.Database.UseCrossTenantConnection();

        act.Should().Throw<InvalidOperationException>().WithMessage("*active cross-tenant scope*");
    }

    // ---------------------------------------------------------------- policy shape (A2, A3)

    [Fact]
    public async Task Policy_IsASinglePredicate_WithoutAnyEscapeClause()
    {
        await using var dataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT qual, with_check FROM pg_policies WHERE tablename = 'rls_order' AND policyname = 'rls_order_tenant_isolation'");
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        var qual = reader.GetString(0);
        qual.Should().Contain("current_setting('app.tenant_id'").And.NotContain("cross_tenant").And.NotContain(" OR ");
        reader.GetString(1).Should().Be(qual);
    }

    [Fact]
    public async Task Policy_UsesTheTenantIndex_UnderRowLevelSecurity()
    {
        await AdminExecuteAsync($"""
            CREATE TABLE rls_indexed (id bigserial PRIMARY KEY, tenant_id uuid NOT NULL, payload text NOT NULL);
            CREATE INDEX rls_indexed_tenant_idx ON rls_indexed (tenant_id);
            INSERT INTO rls_indexed (tenant_id, payload)
                SELECT ('00000000-0000-0000-0000-' || lpad((g % 200)::text, 12, '0'))::uuid, repeat('x', 50)
                FROM generate_series(1, 40000) g;
            GRANT SELECT ON rls_indexed TO {AppRole};
            """);

        var migration = new MigrationBuilder(activeProvider: "Npgsql");
        migration.EnableTenantRowLevelSecurity("rls_indexed");
        foreach (var operation in migration.Operations.OfType<SqlOperation>())
            await AdminExecuteAsync(operation.Sql);
        await AdminExecuteAsync("ANALYZE rls_indexed");

        await using var dataSource = NpgsqlDataSource.Create(AppConnectionString());
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new NpgsqlTenantSessionBinder().BindAsync(
            connection, transaction, Guid.Parse("00000000-0000-0000-0000-000000000007"));

        await using var explain = new NpgsqlCommand("EXPLAIN SELECT * FROM rls_indexed", connection, transaction);
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        string.Join('\n', plan).Should().Contain("rls_indexed_tenant_idx").And.NotContain("Seq Scan");

        await using var count = new NpgsqlCommand("SELECT count(*) FROM rls_indexed", connection, transaction);
        ((long)(await count.ExecuteScalarAsync())!).Should().Be(200);
    }

    [Fact]
    public async Task DisableTenantRowLevelSecurity_DropsBothPolicies_AndUnforcesAndDisablesRls()
    {
        await AdminExecuteAsync("CREATE TABLE rls_down (id int, tenant_id uuid)");
        var up = new MigrationBuilder(activeProvider: "Npgsql");
        up.EnableTenantRowLevelSecurity("rls_down", crossTenantRole: PolicyRole);
        foreach (var operation in up.Operations.OfType<SqlOperation>())
            await AdminExecuteAsync(operation.Sql);

        var down = new MigrationBuilder(activeProvider: "Npgsql");
        down.DisableTenantRowLevelSecurity("rls_down");
        foreach (var operation in down.Operations.OfType<SqlOperation>())
            await AdminExecuteAsync(operation.Sql);

        await using var dataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        (await ScalarAsync<bool>(connection, "SELECT relrowsecurity FROM pg_class WHERE relname = 'rls_down'")).Should().BeFalse();
        (await ScalarAsync<bool>(connection, "SELECT relforcerowsecurity FROM pg_class WHERE relname = 'rls_down'")).Should().BeFalse();
        (await ScalarAsync<long>(connection, "SELECT count(*) FROM pg_policies WHERE tablename = 'rls_down'")).Should().Be(0);
    }

    // ---------------------------------------------------------------- helpers

    private static IQueryable<RlsOrder> Orders(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<RlsTestDbContext>().Orders;

    private static PersistenceContextDependencies Dependencies()
    {
        var clock = new SharedKernel.Primitives.Clocks.SystemClock();
        return PersistenceContextDependencies.Create(requestContext: AnonymousRequestContext.Instance, clock: clock);
    }

    private string AppConnectionString() => RoleConnectionString(AppRole);

    private string RoleConnectionString(string role) =>
        new NpgsqlConnectionStringBuilder(_adminConnectionString) { Username = role, Password = Password }.ConnectionString;

    private RlsHost BuildHost(
        string connectionString,
        string? crossTenantConnectionString = null,
        Action<EfCorePersistenceBuilder<RlsTestDbContext>>? configure = null)
    {
        var services = new ServiceCollection();
        var tenant = new MutableTenantContext();
        var crossTenantScope = new CrossTenantScope(tenant);
        var commands = new ConcurrentQueue<string>();

        services.AddLogging();
        services.AddSingleton<IRequestContext>(tenant);
        services.AddSingleton<ICrossTenantScope>(crossTenantScope);
        services.AddSingleton<ITenantSessionBinder, NpgsqlTenantSessionBinder>();
        if (crossTenantConnectionString is not null)
            services.AddKeyedSingleton(NpgsqlDataSourceKeys.CrossTenant, TestNpgsqlDataSources.Get(crossTenantConnectionString));

        services.AddSharedKernelPostgres<RlsTestDbContext>(new ConfigurationBuilder().Build(), "rls", p =>
        {
            p.UseDataSource(TestNpgsqlDataSources.Get(connectionString))
                .ConfigureProvider(o => o.Retry.Enabled = false)
                .UseMultiTenancy(rowLevelSecurity: true);
            configure?.Invoke(p);

            // Registered after the row-level security extension, so it sees each command's final text.
            p.Services.AddSingleton<IPersistenceOptionsExtension>(new CommandRecorder(commands));
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new RlsHost(provider, tenant, crossTenantScope, commands);
    }

    private async Task SeedAsync(params (Guid TenantId, string Description)[] orders)
    {
        await AdminExecuteAsync("TRUNCATE rls_order");
        foreach (var (tenantId, description) in orders)
            await InsertAsync(Guid.NewGuid(), tenantId, description);
    }

    private async Task SeedAsync(Guid id, (Guid TenantId, string Description) order)
    {
        await AdminExecuteAsync("TRUNCATE rls_order");
        await InsertAsync(id, order.TenantId, order.Description);
    }

    private async Task InsertAsync(Guid id, Guid tenantId, string description)
    {
        await using var dataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var insert = dataSource.CreateCommand("INSERT INTO rls_order (id, tenant_id, description) VALUES ($1, $2, $3)");
        insert.Parameters.Add(new NpgsqlParameter { Value = id });
        insert.Parameters.Add(new NpgsqlParameter { Value = tenantId });
        insert.Parameters.Add(new NpgsqlParameter { Value = description });
        await insert.ExecuteNonQueryAsync();
    }

    private async Task<long> AdminCountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        return await ScalarAsync<long>(connection, "SELECT count(*) FROM rls_order");
    }

    private async Task AdminExecuteAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> BackendPidAsync(DbContext context) =>
        await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull or null ? default! : (T)value;
    }

    private sealed record RlsHost(
        ServiceProvider Provider,
        MutableTenantContext Tenant,
        CrossTenantScope Scope,
        ConcurrentQueue<string> Commands) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class CommandRecorder(ConcurrentQueue<string> commands) : DbCommandInterceptor, IPersistenceOptionsExtension
    {
        public void Apply(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(this);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

internal sealed class RlsOrder : IHasTenant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Description { get; set; } = string.Empty;
}

internal sealed class RlsOrderConfig : IEntityTypeConfiguration<RlsOrder>
{
    public void Configure(EntityTypeBuilder<RlsOrder> builder)
    {
        builder.ToTable("rls_order");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Description).IsRequired();
    }
}

internal sealed class RlsTestDbContext(
    DbContextOptions<RlsTestDbContext> options,
    PersistenceContextDependencies dependencies)
        : TenantedDbContext(options, dependencies)
{
    public DbSet<RlsOrder> Orders => Set<RlsOrder>();
}

internal sealed class MutableTenantContext : IRequestContext
{
    public Guid? TenantId { get; set; }
    public bool IsAuthenticated => false;
    public string? UserId => "rls-ef-test";
    public ActorKind ActorKind => ActorKind.System;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
