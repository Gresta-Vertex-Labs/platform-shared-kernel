using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Extensions;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Persistence.Npgsql.Errors;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Dapper.Tests.Sessions;

/// <summary>
/// <see cref="IDbSessionFactory"/> against a real PostgreSQL: owned transactions, read-only sessions, enlistment
/// in an EF Core unit of work, tenant binding under row-level security (including a PgBouncer-style reuse of one
/// physical connection), the cross-tenant role, and error classification.
/// </summary>
public sealed class DbSessionIntegrationTests : IAsyncLifetime
{
    private const string AppRole = "dapper_app";
    private const string CrossRole = "dapper_cross";
    private const string Password = "dapper_pw";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly PostgreSqlContainerFixture _fixture = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        await AdminExecuteAsync($"""
            CREATE ROLE {AppRole} LOGIN PASSWORD '{Password}';
            CREATE ROLE {CrossRole} LOGIN BYPASSRLS PASSWORD '{Password}';
            CREATE TABLE dapper_log (id serial PRIMARY KEY, message text NOT NULL UNIQUE);
            CREATE TABLE dapper_rls (id uuid PRIMARY KEY, tenant_id uuid NOT NULL, name text NOT NULL UNIQUE);
            CREATE TABLE widgets (id serial PRIMARY KEY, name text NOT NULL);
            GRANT SELECT, INSERT, UPDATE, DELETE ON dapper_log, dapper_rls, widgets TO {AppRole}, {CrossRole};
            GRANT USAGE ON SEQUENCE dapper_log_id_seq, widgets_id_seq TO {AppRole}, {CrossRole};
            """);

        var migration = new MigrationBuilder(activeProvider: "Npgsql");
        migration.EnableTenantRowLevelSecurity("dapper_rls");
        foreach (var operation in migration.Operations.OfType<SqlOperation>())
            await AdminExecuteAsync(operation.Sql);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    // ---------------------------------------------------------------- owned transactions

    [Fact]
    public async Task OwnedSession_Commit_Persists_AndDisposeWithoutCommit_RollsBack()
    {
        await using var host = Build(_fixture.ConnectionString);

        await using (var session = await host.Sessions().OpenAsync())
        {
            session.IsEnlisted.Should().BeFalse();
            await session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES (@m)", new { m = "kept" }));
            await session.CommitAsync();
        }

        await using (var session = await host.Sessions().OpenAsync())
            await session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES (@m)", new { m = "dropped" }));

        (await AdminScalarAsync<long>("SELECT count(*) FROM dapper_log")).Should().Be(1);
    }

    [Fact]
    public async Task ReadOnlySession_RejectsWrites()
    {
        await using var host = Build(_fixture.ConnectionString);
        await using var session = await host.Sessions().OpenReadOnlyAsync();

        session.IsReadOnly.Should().BeTrue();
        var act = () => session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES ('x')"));

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    [Fact]
    public async Task Command_CarriesTheConfiguredDefaultTimeout()
    {
        await using var host = Build(_fixture.ConnectionString, extra: new() { ["SharedKernel:Persistence:Dapper:DefaultCommandTimeoutSeconds"] = "17" });
        await using var session = await host.Sessions().OpenAsync();

        var command = session.Command("SELECT 1");

        command.CommandTimeout.Should().Be(17);
        command.Transaction.Should().BeSameAs(session.Transaction);
    }

    [Fact]
    public async Task Dispose_AfterTheConnectionBroke_DoesNotThrow()
    {
        await using var host = Build(_fixture.ConnectionString);
        var session = await host.Sessions().OpenAsync();
        await session.Connection.ExecuteAsync(session.Command("SELECT 1"));
        await session.Connection.CloseAsync();

        var act = async () => await session.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DapperOnlyService_ResolvesSessions_WithTheAnonymousDefaults()
    {
        await using var host = Build(_fixture.ConnectionString, registerIdentity: false);
        await using var session = await host.Sessions().OpenAsync();

        session.TenantId.Should().BeNull();
        session.Invoking(s => s.RequireTenantId()).Should().Throw<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- ambient EF Core unit of work

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InsideAUnitOfWork_TheSessionEnlists_AndCommitsOrRollsBackWithEfCore(bool commit)
    {
        await using var host = Build(_fixture.ConnectionString, withEfCore: true);
        await using var scope = host.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sessions = scope.ServiceProvider.GetRequiredService<IDbSessionFactory>();

        Func<Task> run = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.Widgets.Add(new Widget { Name = "ef" });
            await context.SaveChangesAsync(ct);

            await using var session = await sessions.OpenAsync(ct);
            session.IsEnlisted.Should().BeTrue();
            session.Transaction.Should().BeSameAs(context.Database.CurrentTransaction!.GetDbTransaction());
            await session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES ('dapper')", cancellationToken: ct));
            await session.CommitAsync(ct); // no-op: the unit of work commits

            if (!commit)
                throw new InvalidOperationException("roll back");
        });

        if (commit)
            await run();
        else
            await run.Should().ThrowAsync<InvalidOperationException>();

        (await AdminScalarAsync<long>("SELECT count(*) FROM widgets")).Should().Be(commit ? 1 : 0);
        (await AdminScalarAsync<long>("SELECT count(*) FROM dapper_log")).Should().Be(commit ? 1 : 0);
    }

    [Fact]
    public async Task EnlistInAmbientTransactionFalse_OpensASeparateTransaction()
    {
        await using var host = Build(_fixture.ConnectionString, withEfCore: true);
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sessions = scope.ServiceProvider.GetRequiredService<IDbSessionFactory>();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await using var session = await sessions.OpenAsync(new DbSessionOptions { EnlistInAmbientTransaction = false }, ct);
            session.IsEnlisted.Should().BeFalse();
        });
    }

    // ---------------------------------------------------------------- row-level security

    [Fact]
    public async Task RowLevelSecurity_BindsTheCallersTenant_ToTheSession()
    {
        await SeedRlsAsync((TenantA, "a1"), (TenantA, "a2"), (TenantB, "b1"));
        await using var host = Build(AppConnectionString(), rowLevelSecurity: true);

        host.Tenant.TenantId = TenantA;
        (await CountRlsAsync(host)).Should().Be(2);

        host.Tenant.TenantId = TenantB;
        (await CountRlsAsync(host)).Should().Be(1);

        host.Tenant.TenantId = null;
        (await CountRlsAsync(host)).Should().Be(0, "no tenant bound matches no rows");
    }

    [Fact]
    public async Task RowLevelSecurity_WriteForAnotherTenant_MapsToForbidden()
    {
        await SeedRlsAsync();
        await using var host = Build(AppConnectionString(), rowLevelSecurity: true);
        host.Tenant.TenantId = TenantA;

        await using var session = await host.Sessions().OpenAsync();
        var result = await PostgresErrorMapping.TryAsync(() => session.Connection.ExecuteAsync(session.Command(
            "INSERT INTO dapper_rls (id, tenant_id, name) VALUES (@id, @tenant, 'x')", new { id = Guid.NewGuid(), tenant = TenantB })));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task RowLevelSecurity_EnlistedSession_BindsTheTenantOnTheAmbientTransaction()
    {
        await SeedRlsAsync((TenantA, "a1"), (TenantB, "b1"));
        await using var host = Build(AppConnectionString(), rowLevelSecurity: true, withEfCore: true);
        host.Tenant.TenantId = TenantB;

        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sessions = scope.ServiceProvider.GetRequiredService<IDbSessionFactory>();

        var names = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await using var session = await sessions.OpenAsync(ct);
            return (await session.Connection.QueryAsync<string>(session.Command("SELECT name FROM dapper_rls", cancellationToken: ct))).ToList();
        });

        names.Should().Equal("b1");
    }

    // A1: one physical connection reused across callers without a reset, as PgBouncer (transaction mode) does.
    [Fact]
    public async Task ReusedPhysicalConnection_CarriesNoTenant_IntoTheNextLease()
    {
        await SeedRlsAsync((TenantA, "a1"), (TenantA, "a2"), (TenantB, "b1"));

        // One physical connection that is never reset between leases, as behind PgBouncer in transaction mode.
        // The validator refuses 'No Reset On Close' under RLS, so the test sets it through the data source hook.
        await using var host = Build(
            AppConnectionString(),
            rowLevelSecurity: true,
            configureDataSource: (_, builder) =>
            {
                builder.ConnectionStringBuilder.MaxPoolSize = 1;
                builder.ConnectionStringBuilder.NoResetOnClose = true;
            });
        var dataSource = host.Provider.GetRequiredService<NpgsqlDataSource>();

        host.Tenant.TenantId = TenantA;
        int pid;
        await using (var session = await host.Sessions().OpenAsync())
        {
            pid = ((NpgsqlConnection)session.Connection).ProcessID;
            (await session.Connection.ExecuteScalarAsync<long>(session.Command("SELECT count(*) FROM dapper_rls"))).Should().Be(2);
            await session.CommitAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            connection.ProcessID.Should().Be(pid, "the same physical connection is reused");
            (await connection.ExecuteScalarAsync<string?>("SELECT current_setting('app.tenant_id', true)")).Should().BeNullOrEmpty();
            (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM dapper_rls")).Should().Be(0, "tenant A must not leak into this lease");
        }

        host.Tenant.TenantId = TenantB;
        await using (var session = await host.Sessions().OpenReadOnlyAsync())
        {
            ((NpgsqlConnection)session.Connection).ProcessID.Should().Be(pid);
            (await session.Connection.QueryAsync<string>(session.Command("SELECT name FROM dapper_rls"))).Should().Equal("b1");
        }
    }

    [Fact]
    public async Task CrossTenantScope_OpensOnTheCrossTenantRole_AndSeesEveryTenant()
    {
        await SeedRlsAsync((TenantA, "a1"), (TenantB, "b1"));
        await using var host = Build(AppConnectionString(), rowLevelSecurity: true, crossTenant: RoleConnectionString(CrossRole));
        host.Tenant.TenantId = TenantA;

        using (host.Scope.Enter("dapper-test"))
            (await CountRlsAsync(host)).Should().Be(2);

        (await CountRlsAsync(host)).Should().Be(1);
    }

    [Fact]
    public async Task CrossTenantScope_WithoutACrossTenantDataSource_IsRefused()
    {
        await using var host = Build(AppConnectionString(), rowLevelSecurity: true);

        using (host.Scope.Enter("dapper-test"))
        {
            var act = () => host.Sessions().OpenAsync();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CrossTenantConnectionString*");
        }
    }

    [Fact]
    public async Task CrossTenantScope_InsideAUnitOfWork_IsRefused()
    {
        await using var host = Build(
            AppConnectionString(), rowLevelSecurity: true, withEfCore: true, crossTenant: RoleConnectionString(CrossRole));
        await using var scope = host.Provider.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sessions = scope.ServiceProvider.GetRequiredService<IDbSessionFactory>();

        var act = () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            using (host.Scope.Enter("dapper-test"))
                await using (await sessions.OpenAsync(ct)) { }
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cross-tenant scope*");
    }

    // ---------------------------------------------------------------- classification

    [Fact]
    public async Task UniqueViolation_MapsToConflict()
    {
        await using var host = Build(_fixture.ConnectionString);
        await using var session = await host.Sessions().OpenAsync();
        await session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES ('dup')"));

        var result = await PostgresErrorMapping.TryAsync(() =>
            session.Connection.ExecuteAsync(session.Command("INSERT INTO dapper_log (message) VALUES ('dup')")));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(PostgreSqlErrorCodes.UniqueViolation);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<long> CountRlsAsync(TestHost host)
    {
        await using var session = await host.Sessions().OpenReadOnlyAsync();
        return await session.Connection.ExecuteScalarAsync<long>(session.Command("SELECT count(*) FROM dapper_rls"));
    }

    private string AppConnectionString() => RoleConnectionString(AppRole);

    private string RoleConnectionString(string role) =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Username = role, Password = Password }.ConnectionString;

    private static TestHost Build(
        string connectionString,
        bool rowLevelSecurity = false,
        string? crossTenant = null,
        bool withEfCore = false,
        bool registerIdentity = true,
        Dictionary<string, string?>? extra = null,
        Action<IServiceProvider, NpgsqlDataSourceBuilder>? configureDataSource = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["SharedKernel:Persistence:Npgsql:ConnectionString"] = connectionString + ";SSL Mode=Disable",
            ["SharedKernel:Persistence:Npgsql:RowLevelSecurity:Enabled"] = rowLevelSecurity ? "true" : "false",
        };
        if (crossTenant is not null)
            settings["SharedKernel:Persistence:Npgsql:RowLevelSecurity:CrossTenantConnectionString"] = crossTenant + ";SSL Mode=Disable";
        foreach (var (key, value) in extra ?? [])
            settings[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var tenant = new MutableTenantContext();
        var scope = new CrossTenantScope(tenant);
        var services = new ServiceCollection();

        services.AddLogging();
        if (registerIdentity)
        {
            services.AddSingleton<IRequestContext>(tenant);
            services.AddSingleton<ICrossTenantScope>(scope);
        }

        services.AddSharedKernelNpgsql(configuration, configureDataSource);
        services.AddSharedKernelDapper(configuration);

        if (withEfCore)
        {
            // Reuses the Npgsql data source registered above (one pool for EF Core and Dapper).
            services.AddSharedKernelPostgres<WidgetDbContext>(configuration, "widgets");
        }

        return new TestHost(services.BuildServiceProvider(), tenant, scope);
    }

    private async Task SeedRlsAsync(params (Guid TenantId, string Name)[] rows)
    {
        await AdminExecuteAsync("TRUNCATE dapper_rls");
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        foreach (var (tenantId, name) in rows)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dapper_rls (id, tenant_id, name) VALUES (@id, @tenantId, @name)",
                new { id = Guid.NewGuid(), tenantId, name });
        }
    }

    private async Task AdminExecuteAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> AdminScalarAsync<T>(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql) ?? default!;
    }

    private sealed class TestHost(ServiceProvider provider, MutableTenantContext tenant, CrossTenantScope scope) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;

        public MutableTenantContext Tenant { get; } = tenant;

        public CrossTenantScope Scope { get; } = scope;

        // Scoped factory: resolved from a fresh scope per call, like a request would.
        public IDbSessionFactory Sessions() => Provider.CreateScope().ServiceProvider.GetRequiredService<IDbSessionFactory>();

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }
}

public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(builder =>
        {
            builder.ToTable("widgets");
            builder.HasKey(w => w.Id);
            builder.Property(w => w.Id).ValueGeneratedOnAdd();
        });
    }
}

public sealed class MutableTenantContext : IRequestContext
{
    public Guid? TenantId { get; set; }

    public bool IsAuthenticated => TenantId is not null;

    public string? UserId => "dapper-test";

    public ActorKind ActorKind => ActorKind.System;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
