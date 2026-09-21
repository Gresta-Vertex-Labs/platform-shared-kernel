using SharedKernel.Application.Context;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.Dapper.ReadModels;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.Dapper.Tests.Integration;

/// <summary>
/// <see cref="TenantSafeDapperCommandService"/> against a real PostgreSQL Testcontainer with a
/// genuine row-level security policy applied: fail-closed with no tenant/scope resolved, tenant
/// isolation on writes, the cross-tenant escape working end to end, and — the scenario this class
/// exists to close — an enlisted write that rebinds correctly even when a cross-tenant scope was
/// entered and exited earlier in the SAME still-open ambient transaction.
/// </summary>
/// <remarks>
/// Connects as a separately-created, genuinely unprivileged role for every write proof — a superuser
/// bypasses row-level security unconditionally, even under <c>FORCE</c>, so asserting isolation over
/// the Testcontainers image's own admin connection would prove nothing.
/// </remarks>
public sealed class TenantSafeDapperCommandServiceIntegrationTests : IAsyncLifetime
{
    private const string WriterRoleName = "tenant_writer";
    private const string WriterRolePassword = "tenant_writer_pw";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _adminDataSource;
    private string _writerConnectionString = string.Empty;

    private int _tenantARowId;
    private int _tenantBRowId;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _adminDataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        var writerConnectionStringBuilder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Username = WriterRoleName,
            Password = WriterRolePassword,
        };
        _writerConnectionString = writerConnectionStringBuilder.ConnectionString;

        await SeedSchemaRowsAndWriterRoleAsync();
    }

    public async Task DisposeAsync()
    {
        if (_adminDataSource is not null)
            await _adminDataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    private async Task SeedSchemaRowsAndWriterRoleAsync()
    {
        await using var connection = await _adminDataSource!.OpenConnectionAsync();

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = $"""
                DROP TABLE IF EXISTS tenant_writable_order;
                CREATE TABLE tenant_writable_order (
                    id SERIAL PRIMARY KEY,
                    tenant_id UUID NOT NULL,
                    description TEXT NOT NULL
                );
                INSERT INTO tenant_writable_order (tenant_id, description) VALUES ('{TenantA}', 'A-original');
                INSERT INTO tenant_writable_order (tenant_id, description) VALUES ('{TenantB}', 'B-original');

                DROP ROLE IF EXISTS {WriterRoleName};
                CREATE ROLE {WriterRoleName} LOGIN PASSWORD '{WriterRolePassword}';
                GRANT SELECT, UPDATE ON tenant_writable_order TO {WriterRoleName};
                GRANT USAGE, SELECT ON SEQUENCE tenant_writable_order_id_seq TO {WriterRoleName};
                """;
            await schema.ExecuteNonQueryAsync();
        }

        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.EnableTenantRowLevelSecurity("tenant_writable_order");

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }

        await using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT id, tenant_id FROM tenant_writable_order ORDER BY id";
        await using var reader = await idCommand.ExecuteReaderAsync();
        await reader.ReadAsync();
        _tenantARowId = reader.GetInt32(0);
        await reader.ReadAsync();
        _tenantBRowId = reader.GetInt32(0);
    }

    private sealed class TenantOrderCommandService(
        IDbConnectionFactory connectionFactory,
        ITenantSessionBinder tenantSessionBinder,
        IRequestContext tenantContext,
        ICrossTenantScope crossTenantScope,
        IAmbientDbTransaction? ambientTransaction = null)
            : TenantSafeDapperCommandService(connectionFactory, tenantSessionBinder, tenantContext, crossTenantScope, ambientTransaction)
    {
        public bool IsCurrentlyEnlisted => IsEnlistedInAmbientTransaction;

        // Deliberately WHERE-less on tenant — RLS, not an application-level predicate, must be what
        // stops this from touching another tenant's row.
        public Task<int> UpdateDescriptionAsync(int id, string description, CancellationToken ct) =>
            ExecuteAsync(
                "UPDATE tenant_writable_order SET description = @description WHERE id = @id",
                new { id, description }, cancellationToken: ct);
    }

    [Fact]
    public async Task UpdateDescription_NoTenantResolved_NoActiveCrossTenantScope_ThrowsBeforeAnySqlRuns()
    {
        var factory = new NpgsqlConnectionFactory(NpgsqlDataSource.Create(_writerConnectionString));
        var tenantContext = new FakeAuditActorContext { TenantId = null };
        var service = new TenantOrderCommandService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance));

        var act = async () => await service.UpdateDescriptionAsync(_tenantARowId, "should-never-apply", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateDescription_OwnTenantsRow_Succeeds()
    {
        var factory = new NpgsqlConnectionFactory(NpgsqlDataSource.Create(_writerConnectionString));
        var tenantContext = new FakeAuditActorContext(tenantId: TenantA);
        var service = new TenantOrderCommandService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance));

        var affected = await service.UpdateDescriptionAsync(_tenantARowId, "A-updated-by-owner", CancellationToken.None);

        affected.Should().Be(1);
    }

    [Fact]
    public async Task UpdateDescription_AnotherTenantsRow_RlsSilentlyMatchesZeroRows()
    {
        var factory = new NpgsqlConnectionFactory(NpgsqlDataSource.Create(_writerConnectionString));
        var tenantContext = new FakeAuditActorContext(tenantId: TenantA);
        var service = new TenantOrderCommandService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance));

        var affected = await service.UpdateDescriptionAsync(_tenantBRowId, "hacked-from-tenant-a", CancellationToken.None);

        affected.Should().Be(0, "the row-level security policy must hide Tenant B's row entirely — not merely reject the write with an error");

        await using var verify = await _adminDataSource!.OpenConnectionAsync();
        await using var verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT description FROM tenant_writable_order WHERE id = @id";
        var idParam = verifyCommand.CreateParameter();
        idParam.ParameterName = "id";
        idParam.Value = _tenantBRowId;
        verifyCommand.Parameters.Add(idParam);
        var description = (string)(await verifyCommand.ExecuteScalarAsync())!;
        description.Should().Be("B-original", "Tenant B's row must remain completely untouched");
    }

    [Fact]
    public async Task UpdateDescription_AnotherTenantsRow_CrossTenantScopeActive_Succeeds()
    {
        var factory = new NpgsqlConnectionFactory(NpgsqlDataSource.Create(_writerConnectionString));
        var tenantContext = new FakeAuditActorContext(tenantId: TenantA);
        var crossTenantScope = new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance);
        var service = new TenantOrderCommandService(factory, new NpgsqlTenantSessionBinder(), tenantContext, crossTenantScope);

        int affected;
        using (crossTenantScope.Enter("integration-test-admin"))
        {
            affected = await service.UpdateDescriptionAsync(_tenantBRowId, "B-updated-under-cross-tenant-scope", CancellationToken.None);
        }

        affected.Should().Be(1, "an explicitly entered ICrossTenantScope must allow writing another tenant's row");
    }

    // ---------------------------------------------------------------------------
    // Ambient-transaction enlistment: shares EF Core's connection/transaction, and rebinds LIVE
    // per statement — the scenario this class exists to prove correct.
    // ---------------------------------------------------------------------------

    private async Task<ServiceProvider> BuildEnlistedProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(TestNpgsqlConfiguration.Create(_writerConnectionString));
        services.AddSingleton<IRequestContext>(new FakeAuditActorContext(tenantId: TenantA));

        // Registered under BOTH the interface (what TenantSafeDapperCommandService's constructor
        // resolves) and the concrete type (so the test itself can call .Enter("integration test"), which is
        // deliberately not part of the ICrossTenantScope interface — see that interface's remarks).
        var crossTenantScope = new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance);
        services.AddSingleton(crossTenantScope);
        services.AddSingleton<ICrossTenantScope>(crossTenantScope);

        services
            .AddSharedKernelPostgres<EnlistedTestDbContext>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), "dapper-tests")
            ;

        services.AddScoped<TenantOrderCommandService>();

        return services.BuildServiceProvider();
    }

    // A deliberately empty model — this suite only needs IUnitOfWork/IAmbientDbTransaction
    // for a connection+transaction to share with the enlisted Dapper command service; it never writes
    // through EF Core itself, so no DbSet/table is needed (and the unprivileged writer role this suite
    // connects as has no CREATE privilege on the schema to provision one).
    public sealed class EnlistedTestDbContext : SharedKernelDbContext
    {
        public EnlistedTestDbContext(DbContextOptions<EnlistedTestDbContext> options, PersistenceContextDependencies dependencies)
            : base(options, dependencies)
        {
        }
    }

    [Fact]
    public async Task UpdateDescription_InsideActiveEfTransaction_IsEnlisted_AndSharesTheSameConnection()
    {
        var provider = await BuildEnlistedProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<TenantOrderCommandService>();

        await RollbackAfterAsync(uow, async () =>
        {

        commandService.IsCurrentlyEnlisted.Should().BeTrue();

        });
    }

    [Fact]
    public async Task UpdateDescription_EnlistedWrite_AfterCrossTenantScopeExitedEarlierInTheSameOpenTransaction_RlsMatchesZeroRows()
    {
        // The exact scenario the H2/H3 review named: a cross-tenant scope is entered and exited
        // EARLIER in an ambient transaction that stays open the whole time, then an ENLISTED Dapper
        // write runs on that same transaction. Without a live rebind immediately before THIS
        // statement, the transaction-scoped RLS escape setting bound while the scope was active could
        // still be in effect — silently letting the write through. TenantSafeDapperCommandService
        // rebinds right before its own statement, so the write correctly sees the scope as inactive.
        var provider = await BuildEnlistedProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<TenantOrderCommandService>();
        var crossTenantScope = scope.ServiceProvider.GetRequiredService<CrossTenantScope>();

        await RollbackAfterAsync(uow, async () =>
        {

        using (crossTenantScope.Enter("earlier-in-the-same-transaction"))
        {
            // Something innocuous happens while the scope is active — the scope is then disposed,
            // but the AMBIENT TRANSACTION stays open for the rest of this test.
            crossTenantScope.IsActive.Should().BeTrue();
        }

        crossTenantScope.IsActive.Should().BeFalse("the scope handle was disposed");

        var affected = await commandService.UpdateDescriptionAsync(
            _tenantBRowId, "hacked-after-scope-exit-same-transaction", CancellationToken.None);

        affected.Should().Be(
            0, "the enlisted write must rebind LIVE and see the scope as inactive, not reuse a stale " +
                "transaction-scoped binding left over from earlier in the same still-open transaction");

        });
    }

    [Fact]
    public async Task UpdateDescription_EnlistedWrite_WhileCrossTenantScopeActive_Succeeds()
    {
        // Positive counterpart of the test above: entering the scope AROUND the enlisted call (not
        // earlier, then exited) must still work correctly for a write sharing EF's own transaction.
        var provider = await BuildEnlistedProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<TenantOrderCommandService>();
        var crossTenantScope = scope.ServiceProvider.GetRequiredService<CrossTenantScope>();

        await RollbackAfterAsync(uow, async () =>
        {

        int affected;
        using (crossTenantScope.Enter("around-the-enlisted-call"))
        {
            affected = await commandService.UpdateDescriptionAsync(
                _tenantBRowId, "cross-tenant-write-inside-active-scope", CancellationToken.None);
        }

        affected.Should().Be(1, "the enlisted write must see the scope as active while it genuinely is");

        });
    }

    // Runs body inside a unit-of-work transaction and rolls it back afterwards (a failed Result
    // commits nothing) — the P-558 replacement for the removed BeginTransactionAsync/RollbackAsync pair.
    private static Task RollbackAfterAsync(IUnitOfWork uow, Func<Task> body) =>
        uow.ExecuteInTransactionAsync(async _ =>
        {
            await body();
            return SharedKernel.Primitives.Results.Result.Failure(
                SharedKernel.Primitives.Errors.Error.Conflict("test.rollback", "Roll the test transaction back."));
        });
}
