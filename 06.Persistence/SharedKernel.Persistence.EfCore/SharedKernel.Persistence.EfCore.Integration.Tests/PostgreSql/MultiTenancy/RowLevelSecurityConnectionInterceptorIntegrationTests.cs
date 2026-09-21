using SharedKernel.Core.Exceptions;
using SharedKernel.Application.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.Npgsql.Context;
using SharedKernel.Persistence.EfCore.Migrations;
using SharedKernel.Testing.Containers;

using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.PostgreSql.MultiTenancy;

/// <summary>
/// <see cref="RowLevelSecurityConnectionInterceptor"/> against a real
/// PostgreSQL Testcontainer, connecting through a genuinely unprivileged role (superusers bypass RLS
/// unconditionally, even under FORCE — see the class remarks in
/// <c>TenantSafeDapperReadServiceIntegrationTests</c> for the same established pattern): EF reads and
/// writes on a FORCE-RLS table are scoped to the current tenant even when EF's OWN application-level
/// tenant query filter is bypassed (<c>IgnoreQueryFilters([Tenant])</c>), a caller with no tenant bound
/// sees zero rows and has every write rejected, and the <see cref="ICrossTenantScope"/> escape clause
/// gives real cross-tenant visibility inside the scope and none outside it.
/// </summary>
public sealed class RowLevelSecurityConnectionInterceptorIntegrationTests : IAsyncLifetime
{
    private const string ReaderRoleName = "rls_ef_reader";
    private const string ReaderRolePassword = "rls_ef_reader_pw";
    private const string DatabaseName = "sk_p557_rls_ef";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly PostgreSqlContainerFixture _fixture = new();
    private string _adminConnectionString = string.Empty;
    private string _readerConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        _adminConnectionString =
            new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;
        _readerConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Username = ReaderRoleName,
            Password = ReaderRolePassword,
        }.ConnectionString;

        await SeedSchemaRlsPolicyAndReaderRoleAsync();
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task SeedSchemaRlsPolicyAndReaderRoleAsync()
    {
        // EnsureCreated over the admin (superuser) connection — builds rls_order from the SAME model
        // RlsTestDbContext exposes, matching real column/table names exactly.
        var adminOptionsBuilder = new DbContextOptionsBuilder<RlsTestDbContext>();
        adminOptionsBuilder.UsePostgreSQL(TestNpgsqlDataSources.Get(_adminConnectionString), o => o.Retry.Enabled = false);
        var adminOptions = adminOptionsBuilder.Options;

        var actor = new FixedActorContext();
        var clock = new SharedKernel.Primitives.Clocks.SystemClock();
        await using (var adminCtx = new RlsTestDbContext(
            adminOptions,
            new PersistenceContextDependencies(
                new AuditInterceptor(actor, clock),
                new SoftDeleteInterceptor(clock),
                new ConcurrencyInterceptor())))
        {
            await adminCtx.Database.EnsureCreatedAsync();
        }

        await using var adminDataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var connection = await adminDataSource.OpenConnectionAsync();

        await using (var role = connection.CreateCommand())
        {
            role.CommandText = $"""
                DROP ROLE IF EXISTS {ReaderRoleName};
                CREATE ROLE {ReaderRoleName} LOGIN PASSWORD '{ReaderRolePassword}';
                GRANT SELECT, INSERT, UPDATE, DELETE ON rls_order TO {ReaderRoleName};
                """;
            await role.ExecuteNonQueryAsync();
        }

        // Apply the RLS migration helper's REAL generated SQL — this is the actual enforcement
        // mechanism under test, not a hand-rolled policy re-derived for the test.
        var migrationBuilder = new MigrationBuilder(activeProvider: "Npgsql");
        migrationBuilder.EnableTenantRowLevelSecurity("rls_order");

        foreach (var operation in migrationBuilder.Operations.OfType<SqlOperation>())
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = operation.Sql;
            await ddl.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedOrdersAsync(params (Guid TenantId, string Description)[] orders)
    {
        await using var adminDataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var connection = await adminDataSource.OpenConnectionAsync();

        foreach (var (tenantId, description) in orders)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO rls_order (id, tenant_id, description) VALUES ($1, $2, $3)";
            insert.Parameters.Add(new NpgsqlParameter { Value = Guid.NewGuid() });
            insert.Parameters.Add(new NpgsqlParameter { Value = tenantId });
            insert.Parameters.Add(new NpgsqlParameter { Value = description });
            await insert.ExecuteNonQueryAsync();
        }
    }

    // withMultiTenancy: false builds a context with ONLY.WithRowLevelSecurity() — no app-level
    // TenantWriteGuardInterceptor — so a write test against this configuration isolates row-level
    // security as the SOLE enforcement mechanism, independent of the platform's own application-level
    // guard (already proven, W2).
    private (ServiceProvider Provider, MutableTenantContext TenantContext, CrossTenantScope CrossTenantScope) BuildReaderProvider(
        bool withMultiTenancy)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<MutableTenantContext>();
        services.AddSingleton<IRequestContext>(sp => sp.GetRequiredService<MutableTenantContext>());
        services.AddSingleton<CrossTenantScope>();
        services.AddSingleton<ICrossTenantScope>(sp => sp.GetRequiredService<CrossTenantScope>());
        services.AddSingleton<ITenantSessionBinder, NpgsqlTenantSessionBinder>();

        var builder = services
            .AddSharedKernelEfCore<RlsTestDbContext>((sp, options) => options.UsePostgreSQL(TestNpgsqlDataSources.Get(_readerConnectionString), o => o.Retry.Enabled = false));

        if (withMultiTenancy)
            builder.WithMultiTenancy();

        builder.WithRowLevelSecurity().Build();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        return (provider, provider.GetRequiredService<MutableTenantContext>(), provider.GetRequiredService<CrossTenantScope>());
    }

    [Fact]
    public async Task EfRead_TenantABound_EvenBypassingEfsOwnTenantFilter_SeesOnlyTenantARows()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var rows = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TenantId == TenantA);
    }

    [Fact]
    public async Task EfRead_TenantBBound_EvenBypassingEfsOwnTenantFilter_SeesOnlyTenantBRows()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = TenantB;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var rows = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

        rows.Should().HaveCount(1);
        rows.Should().OnlyContain(r => r.TenantId == TenantB);
    }

    [Fact]
    public async Task EfRead_NoTenantBound_EvenBypassingEfsOwnTenantFilter_SeesZeroRows()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantB, "B-1"));

        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = null;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var rows = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task EfWrite_RowLevelSecurityOnlyConfiguration_NoTenantBound_InsertIsRejected_IndependentlyOfTheAppLevelGuard()
    {
        // withMultiTenancy: false — TenantWriteGuardInterceptor is NOT registered in this
        // configuration, so this proves row-level security ALONE rejects the write; the app-level
        // guard's equivalent behavior is already proven independently (W2).
        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: false);
        await using var _1 = provider;
        tenantContext.TenantId = null;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        ctx.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "should-be-rejected" });

        var act = async () => await ctx.SaveChangesAsync();

        // 42501 is classified as Forbidden (P-558), keeping the provider exception chain.
        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.InnerException.Should().BeOfType<DbUpdateException>();
        var postgres = exception.Which.InnerException!.InnerException.Should().BeOfType<PostgresException>().Subject;
        postgres.MessageText.Should().Contain("row-level security");
    }

    [Fact]
    public async Task EfWrite_RowLevelSecurityOnlyConfiguration_TenantABound_EntityClaimsTenantB_InsertIsRejected()
    {
        // The caller IS bound to a tenant, but the ROW being inserted claims a DIFFERENT one — the
        // classic cross-tenant write attempt, rejected purely by the database policy in this
        // RLS-only (no app write guard) configuration.
        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: false);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        ctx.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantB, Description = "cross-tenant-write" });

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EfWrite_TenantABound_EntityClaimsTenantA_InsertSucceeds()
    {
        // Control: the same RLS-only configuration accepts a well-formed, correctly-tenanted write.
        var (provider, tenantContext, _) = BuildReaderProvider(withMultiTenancy: false);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();
        var order = new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantA, Description = "well-formed" };
        ctx.Orders.Add(order);

        await ctx.SaveChangesAsync();

        var reloaded = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant])
            .SingleOrDefaultAsync(o => o.Id == order.Id);
        reloaded.Should().NotBeNull();
    }

    [Fact]
    public async Task EfRead_CrossTenantScopeActive_EvenBypassingEfsOwnTenantFilter_SeesEveryTenantsRows()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, crossTenantScope) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = null;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        using (crossTenantScope.Enter())
        {
            var rows = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

            rows.Should().HaveCount(3);
            rows.Select(r => r.TenantId).Distinct().Should().BeEquivalentTo([TenantA, TenantB]);
        }
    }

    [Fact]
    public async Task EfRead_AfterCrossTenantScopeDisposed_ReturnsToNormalTenantIsolation()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, crossTenantScope) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        using (crossTenantScope.Enter())
        {
            var rowsInside = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();
            rowsInside.Should().HaveCount(3);
        }

        var rowsOutside = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();
        rowsOutside.Should().HaveCount(2);
        rowsOutside.Should().OnlyContain(r => r.TenantId == TenantA);
    }

    // ---------------------------------------------------------------------------
    // RowLevelSecurityCommandInterceptor: the connection-scoped bind alone is not exact once a
    // connection's lease spans more than one statement under an explicit transaction — these three
    // tests prove the per-command re-bind closes that gap. The FIRST of the three below is the
    // scenario the regression comment on EfRead_AfterCrossTenantScopeDisposed_ReturnsToNormalTenantIsolation
    // notes as untested: that test only passes because EF closes and reopens the connection between
    // two untransacted queries, never proving the scope transition WITHIN one still-open connection.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EfRead_CrossTenantScopeEnteredAfterTheTransactionAlreadyOpened_StillSeesEveryTenantsRows()
    {
        // The connection opens (and RowLevelSecurityConnectionInterceptor's ConnectionOpened binds)
        // BEFORE the scope is entered — without a per-command re-bind, the escape clause bound at
        // connection-open time would never move, and this read would silently return zero/tenant-only
        // rows instead of every tenant's rows.
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, crossTenantScope) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = null;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        await using var transaction = await ctx.Database.BeginTransactionAsync();

        using (crossTenantScope.Enter())
        {
            var rows = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

            rows.Should().HaveCount(
                3, "the scope was entered AFTER the connection/transaction already opened — the " +
                    "per-command re-bind, not the one-time connection-open bind, is what must catch this");
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task EfRead_CrossTenantScopeExitedWhileTheTransactionIsStillOpen_NextReadOnTheSameTransactionIsIsolatedAgain()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        var (provider, tenantContext, crossTenantScope) = BuildReaderProvider(withMultiTenancy: true);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        await using var transaction = await ctx.Database.BeginTransactionAsync();

        using (crossTenantScope.Enter())
        {
            var rowsInsideScope = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();
            rowsInsideScope.Should().HaveCount(3);
        }

        // Scope handle disposed, but the TRANSACTION — and therefore the connection — never closed.
        // A stale, still-bound escape clause from earlier in this same transaction would leak into
        // this second read if only the connection-open bind existed.
        var rowsAfterScopeExit = await ctx.Orders.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync();

        rowsAfterScopeExit.Should().HaveCount(2);
        rowsAfterScopeExit.Should().OnlyContain(r => r.TenantId == TenantA);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task EfWrite_CrossTenantScopeExitedWhileTheTransactionIsStillOpen_SubsequentCrossTenantInsertIsRejectedAgain()
    {
        // Write-side counterpart: an INSERT that would have been permitted while the scope was active
        // must be rejected again once the scope exits, even though the ambient transaction (and its
        // connection) never closed in between.
        var (provider, tenantContext, crossTenantScope) = BuildReaderProvider(withMultiTenancy: false);
        await using var _1 = provider;
        tenantContext.TenantId = TenantA;

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RlsTestDbContext>();

        await using var transaction = await ctx.Database.BeginTransactionAsync();

        using (crossTenantScope.Enter())
        {
            ctx.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantB, Description = "permitted-inside-scope" });
            await ctx.SaveChangesAsync();
        }

        ctx.Orders.Add(new RlsOrder { Id = Guid.NewGuid(), TenantId = TenantB, Description = "rejected-after-scope-exit" });
        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<ForbiddenException>(
            "the escape clause must not still be bound on this transaction after the scope handle was disposed");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AsSuperuser_WithNoTenantBound_SeesEveryRow_ConfirmingRlsAloneScopesTheReaderRole()
    {
        await SeedOrdersAsync((TenantA, "A-1"), (TenantA, "A-2"), (TenantB, "B-1"));

        await using var adminDataSource = NpgsqlDataSource.Create(_adminConnectionString);
        await using var connection = await adminDataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM rls_order";
        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(3);
    }
}

// ---------------------------------------------------------------------------
// Minimal fixtures — a TenantedDbContext with one entity, deliberately relying on the base
// OnModelCreating (no manual filtering) now that the assembly scan scopes itself
// automatically.
// ---------------------------------------------------------------------------

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

internal sealed class FixedActorContext : IRequestContext
{
    public bool IsAuthenticated => false;
    public string? UserId => "rls-ef-test";
    public Guid? TenantId => null;
    public ActorKind ActorKind => ActorKind.System;

    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
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
