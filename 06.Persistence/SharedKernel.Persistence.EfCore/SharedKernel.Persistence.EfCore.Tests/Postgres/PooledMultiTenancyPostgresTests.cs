using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Tests.Extensions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// The authoritative proof that
/// <c>EfCorePersistenceBuilder.WithDbContextPooling()</c> combined with <c>.WithMultiTenancy()</c>
/// (the real fix replacing the former hard incompatibility guard) is safe under
/// REAL concurrency against REAL PostgreSQL: two GENUINELY concurrent scopes of different tenants
/// sharing one small pool never cross-contaminate, a background scope with no tenant registered
/// fails closed (reads nothing, writes nothing), a background scope inside an active
/// <see cref="ICrossTenantScope"/> can deliberately read across tenants, and
/// <see cref="IDbContextFactory{TContext}"/> resolved directly from a background scope (the shape a
/// hosted service/worker uses) attaches that scope's own tenant correctly.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class PooledMultiTenancyPostgresTests
{
    private const string DatabaseName = "sk_p557_pooled_multitenancy";

    private readonly PostgreSqlContainerFixture _fixture;

    public PooledMultiTenancyPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private ServiceProvider BuildProvider(int poolSize)
    {
        var services = new ServiceCollection();

        services.AddScoped<MutableTestActorContext>();
        services.AddScoped<ICurrentActorContext>(sp => sp.GetRequiredService<MutableTestActorContext>());
        services.AddScoped<MutableTestTenantContext>();
        services.AddScoped<ICurrentTenantContext>(sp => sp.GetRequiredService<MutableTestTenantContext>());

        services
            .AddSharedKernelEfCore<PgTestDbContext>(opts => opts.UsePostgreSQL(ConnectionString))
            .WithMultiTenancy()
            .WithDbContextPooling(poolSize: poolSize)
            .Build();

        // ValidateScopes/ValidateOnBuild — the same technique that proved the pooling+actor fix
        // (DbContextPoolingTests) and the pooling+multitenancy wiring fix (this wave) never resolves
        // a genuinely Scoped service from EF Core's pool-level activator.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public async Task TwoConcurrentScopes_DifferentTenants_SharingOnePool_NeverCrossContaminate()
    {
        await using var provider = BuildProvider(poolSize: 2);

        using (var setupScope = provider.CreateScope())
            await setupScope.ServiceProvider.GetRequiredService<PgTestDbContext>().Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var codeA = $"pooled-concurrent-a-{Guid.NewGuid():N}";
        var codeB = $"pooled-concurrent-b-{Guid.NewGuid():N}";

        // Act — GENUINELY concurrent: both tasks run their whole scope lifetime (resolve, mutate
        // tenant, write, read-back-and-assert) in parallel against a 2-slot pool, so the pool is
        // under real contention, not merely sequential reuse.
        async Task RunForTenant(Guid tenantId, string code)
        {
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantId;

            var ctx = scope.ServiceProvider.GetRequiredService<PgTestDbContext>();
            ctx.Orders.Add(new PgOrderAggregate(
                PgOrderId.New(), tenantId, "Concurrent", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctx.SaveChangesAsync();

            // Small delay so both tasks' write+read windows genuinely overlap in wall-clock time.
            await Task.Delay(50);

            var visible = await ctx.Orders.Select(o => o.Code).ToListAsync();
            visible.Should().Contain(code);
            visible.Should().NotContain(code == codeA ? codeB : codeA,
                "a pooled instance leased to THIS tenant's scope must never leak the other concurrently-running tenant's row");
        }

        await Task.WhenAll(RunForTenant(tenantA, codeA), RunForTenant(tenantB, codeB));
    }

    [Fact]
    public async Task BackgroundScope_NoTenantRegistered_FailsClosed_ReadsNothingWritesNothing()
    {
        await using var provider = BuildProvider(poolSize: 4);

        using (var setupScope = provider.CreateScope())
            await setupScope.ServiceProvider.GetRequiredService<PgTestDbContext>().Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        var code = $"pooled-bg-notenant-{Guid.NewGuid():N}";

        using (var writerScope = provider.CreateScope())
        {
            writerScope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantId;
            var ctx = writerScope.ServiceProvider.GetRequiredService<PgTestDbContext>();
            ctx.Orders.Add(new PgOrderAggregate(
                PgOrderId.New(), tenantId, "Written", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctx.SaveChangesAsync();
        }

        // Act — a background scope that never sets MutableTestTenantContext.TenantId at all.
        // MutableTestTenantContext.TenantId defaults to null (the builder's own fail-closed
        // default), exactly modelling an unattached background worker.
        using var backgroundScope = provider.CreateScope();
        var bgCtx = backgroundScope.ServiceProvider.GetRequiredService<PgTestDbContext>();

        var visible = await bgCtx.Orders.Where(o => o.Code == code).ToListAsync();
        visible.Should().BeEmpty("a background scope with no tenant attached must read nothing — fail closed");

        // Writing under no tenant must be rejected outright (TenantWriteGuardInterceptor), not
        // silently accepted under some default tenant.
        bgCtx.Orders.Add(new PgOrderAggregate(
            PgOrderId.New(), tenantId, "ShouldBeRejected", $"{code}-write", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
        var act = () => bgCtx.SaveChangesAsync();
        await act.Should().ThrowAsync<ForbiddenException>(
            "a background scope with no tenant attached must fail closed on writes too, never silently succeed");
    }

    [Fact]
    public async Task BackgroundScope_InsideCrossTenantScope_CanReadAcrossTenants()
    {
        await using var provider = BuildProvider(poolSize: 4);

        using (var setupScope = provider.CreateScope())
            await setupScope.ServiceProvider.GetRequiredService<PgTestDbContext>().Database.EnsureCreatedAsync();

        var ownerTenant = Guid.NewGuid();
        var readerTenant = Guid.NewGuid();
        var orderId = PgOrderId.New();
        var code = $"pooled-bg-crossscope-{Guid.NewGuid():N}";

        using (var writerScope = provider.CreateScope())
        {
            writerScope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = ownerTenant;
            var ctx = writerScope.ServiceProvider.GetRequiredService<PgTestDbContext>();
            ctx.Orders.Add(new PgOrderAggregate(orderId, ownerTenant, "Owned", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctx.SaveChangesAsync();
        }

        // Act — a background scope bound to a DIFFERENT tenant, with an explicitly entered
        // ICrossTenantScope, using TenantedRepository's own admin/recovery path.
        using var backgroundScope = provider.CreateScope();
        backgroundScope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = readerTenant;
        var bgCtx = backgroundScope.ServiceProvider.GetRequiredService<PgTestDbContext>();
        var crossTenantScope = backgroundScope.ServiceProvider.GetRequiredService<ICrossTenantScope>();
        var repo = new PgOrderRepository(bgCtx, crossTenantScope);

        // Enter() is a CrossTenantScope-concrete-class member, not on the ICrossTenantScope
        // interface (which deliberately exposes only the read side, IsActive) — the default
        // registration's concrete type, so this cast always succeeds unless a consumer registered
        // its own ICrossTenantScope implementation, which this test does not.
        using (((SharedKernel.Persistence.Abstractions.Context.CrossTenantScope)crossTenantScope).Enter())
        {
            var found = await repo.GetByIdForTenantAsync(orderId, ownerTenant);
            found.Should().NotBeNull("an explicitly entered ICrossTenantScope must allow a pooled background scope to read another tenant's row");
        }
    }

    [Fact]
    public async Task FactoryResolvedFromBackgroundScope_AttachesThatScopesTenant()
    {
        await using var provider = BuildProvider(poolSize: 4);

        using (var setupScope = provider.CreateScope())
            await setupScope.ServiceProvider.GetRequiredService<PgTestDbContext>().Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        var code = $"pooled-bg-factory-{Guid.NewGuid():N}";

        using (var writerScope = provider.CreateScope())
        {
            writerScope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantId;
            var ctx = writerScope.ServiceProvider.GetRequiredService<PgTestDbContext>();
            ctx.Orders.Add(new PgOrderAggregate(
                PgOrderId.New(), tenantId, "ViaFactory", code, "St", "City", new SharedKernel.Primitives.Clocks.SystemClock()));
            await ctx.SaveChangesAsync();
        }

        // Act — the shape a hosted service/background worker actually uses: resolve
        // IDbContextFactory<TContext>, not TContext directly, from its OWN scope.
        using var backgroundScope = provider.CreateScope();
        backgroundScope.ServiceProvider.GetRequiredService<MutableTestTenantContext>().TenantId = tenantId;
        var factory = backgroundScope.ServiceProvider.GetRequiredService<IDbContextFactory<PgTestDbContext>>();

        await using var ctxFromFactory = await factory.CreateDbContextAsync();
        var visible = await ctxFromFactory.Orders.Where(o => o.Code == code).ToListAsync();

        visible.Should().ContainSingle(
            "a context obtained via IDbContextFactory<TContext> from a background scope must see THAT scope's own tenant, exactly like direct TContext injection does");
    }
}
