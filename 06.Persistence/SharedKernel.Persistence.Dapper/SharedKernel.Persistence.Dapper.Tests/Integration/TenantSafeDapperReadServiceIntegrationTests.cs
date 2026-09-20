using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.ReadModels;
using SharedKernel.Persistence.Npgsql.Connections;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.PostgreSQL.Migrations;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.Dapper.Tests.Integration;

public sealed class TenantOrderRow
{
    public int Id { get; init; }
    public Guid TenantId { get; init; }
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// <see cref="TenantSafeDapperReadService"/> against a real PostgreSQL Testcontainer with
/// a genuine row-level security policy applied (via
/// <see cref="RowLevelSecurityMigrationBuilderExtensions.EnableTenantRowLevelSecurity"/>'s generated
/// SQL): tenant A cannot read tenant B's rows even with a WHERE-less query, and a call with no tenant
/// resolved and no active cross-tenant scope fails closed before any SQL executes.
/// </summary>
/// <remarks>
/// <strong>Superusers always bypass row-level security, unconditionally — even under FORCE.</strong>
/// The Testcontainers PostgreSQL image's own configured user is created via <c>initdb</c> and is a
/// superuser, so seeding/DDL happens over that connection (where RLS is irrelevant either way), but
/// the actual read proof below connects as a separately-created, genuinely unprivileged
/// <c>tenant_reader</c> role — only a non-superuser role is a meaningful proof that the policy itself
/// (not merely "the query happened to filter") is what scopes the result.
/// </remarks>
public sealed class TenantSafeDapperReadServiceIntegrationTests : IAsyncLifetime
{
    private const string ReaderRoleName = "tenant_reader";
    private const string ReaderRolePassword = "tenant_reader_pw";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly PostgreSqlContainerFixture _fixture = new();
    private NpgsqlDataSource? _adminDataSource;
    private NpgsqlDataSource? _readerDataSource;

    public async Task InitializeAsync()
    {
        // Process-wide, idempotent to call repeatedly — see DapperTypeHandlers.Apply's
        // own remarks on why this is process-global Dapper state, not a per-test/per-instance setting.
        SharedKernel.Persistence.Dapper.TypeHandlers.DapperTypeHandlers.Apply();

        await _fixture.InitializeAsync();
        _adminDataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        var readerConnectionStringBuilder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Username = ReaderRoleName,
            Password = ReaderRolePassword,
        };
        _readerDataSource = NpgsqlDataSource.Create(readerConnectionStringBuilder.ConnectionString);

        await SeedSchemaRowsAndReaderRoleAsync();
    }

    public async Task DisposeAsync()
    {
        if (_adminDataSource is not null)
            await _adminDataSource.DisposeAsync();

        if (_readerDataSource is not null)
            await _readerDataSource.DisposeAsync();

        await _fixture.DisposeAsync();
    }

    private async Task SeedSchemaRowsAndReaderRoleAsync()
    {
        await using var connection = await _adminDataSource!.OpenConnectionAsync();

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = $"""
                DROP TABLE IF EXISTS tenant_order;
                CREATE TABLE tenant_order (
                    id SERIAL PRIMARY KEY,
                    tenant_id UUID NOT NULL,
                    description TEXT NOT NULL
                );
                INSERT INTO tenant_order (tenant_id, description) VALUES
                    ('{TenantA}', 'A-order-1'),
                    ('{TenantA}', 'A-order-2'),
                    ('{TenantB}', 'B-order-1');

                DROP ROLE IF EXISTS {ReaderRoleName};
                CREATE ROLE {ReaderRoleName} LOGIN PASSWORD '{ReaderRolePassword}';
                GRANT SELECT ON tenant_order TO {ReaderRoleName};
                """;
            await schema.ExecuteNonQueryAsync();
        }

        // Apply the RLS migration helper's REAL generated SQL — this is the actual enforcement
        // mechanism under test, not a hand-rolled policy re-derived for the test.
        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.EnableTenantRowLevelSecurity("tenant_order");

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }
    }

    private sealed class TenantOrderReadService(
        IDbConnectionFactory connectionFactory,
        ITenantSessionBinder tenantSessionBinder,
        ICurrentTenantContext tenantContext,
        ICrossTenantScope crossTenantScope)
            : TenantSafeDapperReadService(connectionFactory, tenantSessionBinder, tenantContext, crossTenantScope)
    {
        // Deliberately WHERE-less — RLS, not this query's own WHERE clause, must be what scopes the
        // result to the bound tenant. No column aliasing needed: Dapper's
        // DefaultTypeMap.MatchNamesWithUnderscores is turned on by default via AddSharedKernelDapper/
        // DapperTypeHandlers.Apply, so tenant_id binds directly to TenantId.
        public Task<IReadOnlyList<TenantOrderRow>> GetAllOrdersAsync(CancellationToken ct) =>
            QueryAsync<TenantOrderRow>("SELECT id, tenant_id, description FROM tenant_order", null, cancellationToken: ct);
    }

    [Fact]
    public async Task GetAllOrders_TenantA_NeverSeesTenantBRows_EvenWithAWhereLessQuery()
    {
        var factory = new NpgsqlConnectionFactory(_readerDataSource!);
        var tenantContext = new FakeAuditActorContext(tenantId: TenantA);
        var service = new TenantOrderReadService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope());

        var rows = await service.GetAllOrdersAsync(CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == TenantA);
    }

    [Fact]
    public async Task GetAllOrders_TenantB_NeverSeesTenantARows()
    {
        var factory = new NpgsqlConnectionFactory(_readerDataSource!);
        var tenantContext = new FakeAuditActorContext(tenantId: TenantB);
        var service = new TenantOrderReadService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope());

        var rows = await service.GetAllOrdersAsync(CancellationToken.None);

        rows.Should().HaveCount(1);
        rows.Should().OnlyContain(r => r.TenantId == TenantB);
    }

    [Fact]
    public async Task GetAllOrders_NoTenantResolved_NoActiveCrossTenantScope_ThrowsBeforeAnySqlRuns()
    {
        var factory = new NpgsqlConnectionFactory(_readerDataSource!);
        var tenantContext = new FakeAuditActorContext { TenantId = null };
        var service = new TenantOrderReadService(factory, new NpgsqlTenantSessionBinder(), tenantContext, new CrossTenantScope());

        var act = async () => await service.GetAllOrdersAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAllOrders_CrossTenantScopeActive_NoTenantResolved_SeesEveryTenantsRows()
    {
        // The RLS policy's cross-tenant escape clause — set by NpgsqlTenantSessionBinder
        // only while an ICrossTenantScope is entered — gives REAL cross-tenant visibility, not merely a
        // fail-closed "no rows" outcome.
        var factory = new NpgsqlConnectionFactory(_readerDataSource!);
        var tenantContext = new FakeAuditActorContext { TenantId = null };
        var crossTenantScope = new CrossTenantScope();
        var service = new TenantOrderReadService(factory, new NpgsqlTenantSessionBinder(), tenantContext, crossTenantScope);

        using (crossTenantScope.Enter())
        {
            var rows = await service.GetAllOrdersAsync(CancellationToken.None);

            rows.Should().HaveCount(3);
            rows.Select(r => r.TenantId).Distinct().Should().BeEquivalentTo([TenantA, TenantB]);
        }
    }

    [Fact]
    public async Task GetAllOrders_AfterCrossTenantScopeDisposed_ReturnsToNormalTenantIsolation()
    {
        // Same service instance, same underlying pooled connection factory: proves the escape clause
        // does not leak past the scope that activated it — outside the "using", tenant A is isolated
        // again exactly as the non-cross-tenant tests above prove.
        var factory = new NpgsqlConnectionFactory(_readerDataSource!);
        var tenantContext = new FakeAuditActorContext(tenantId: TenantA);
        var crossTenantScope = new CrossTenantScope();
        var service = new TenantOrderReadService(factory, new NpgsqlTenantSessionBinder(), tenantContext, crossTenantScope);

        using (crossTenantScope.Enter())
        {
            var rowsInside = await service.GetAllOrdersAsync(CancellationToken.None);
            rowsInside.Should().HaveCount(3);
        }

        var rowsOutside = await service.GetAllOrdersAsync(CancellationToken.None);
        rowsOutside.Should().HaveCount(2);
        rowsOutside.Should().OnlyContain(r => r.TenantId == TenantA);
    }

    [Fact]
    public async Task GetAllOrders_AsSuperuser_WithNoTenantBound_SeesEveryRow_ConfirmingRlsAloneScopesTheReaderRole()
    {
        // A control test: the SAME table, over the ADMIN (superuser) connection, with NO tenant
        // bound at all, sees every row — proving the isolation the tests above observe genuinely
        // comes from the RLS policy applying to the unprivileged tenant_reader role, not from some
        // other incidental filter.
        await using var connection = await _adminDataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tenant_order";
        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(3);
    }
}
