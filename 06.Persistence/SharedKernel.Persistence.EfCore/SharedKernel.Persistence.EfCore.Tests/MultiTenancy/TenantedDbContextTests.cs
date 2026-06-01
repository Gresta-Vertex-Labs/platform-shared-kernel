using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.MultiTenancy;

public sealed class TenantedDbContextTests
{
    [Fact]
    public async Task Query_WithTenantFilter_ReturnsOnlyCurrentTenantEntities()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        // Use a shared SQLite file so we can seed and query
        var dbName = $"tenanted-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        // Seed with no tenant filter (use IgnoreQueryFilters workaround via direct add)
        // We create a context with tenant1 to seed tenant1 data, then tenant2
        var tenantService1 = TestDbContextFactory.CreateTenantService(tenant1);
        var tenantService2 = TestDbContextFactory.CreateTenantService(tenant2);

        var options1 = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .Options;
        var options2 = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .Options;

        var userCtx = TestDbContextFactory.CreateUserContext("user");
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit1 = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDel1 = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var conc1 = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();
        var audit2 = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDel2 = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var conc2 = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();

        await using var ctx1 = new TenantedTestDbContext(options1, audit1, softDel1, conc1, tenantService1);
        ctx1.Database.EnsureCreated();

        // Add entities for tenant1 and tenant2 via ctx1 (bypasses tenant filter for adds)
        var id1 = TenantedTestId.New();
        var id2 = TenantedTestId.New();
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id1, "T1Entity", tenant1, new SystemClock()));
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2Entity", tenant2, new SystemClock()));
        await ctx1.SaveChangesAsync();

        // Act — query with tenant2 filter
        await using var ctx2 = new TenantedTestDbContext(options2, audit2, softDel2, conc2, tenantService2);
        var tenant2Entities = await ctx2.TenantedAggregates.ToListAsync();

        // Assert
        tenant2Entities.Should().HaveCount(1);
        tenant2Entities[0].TenantId.Should().Be(tenant2);
        tenant2Entities[0].Name.Should().Be("T2Entity");
    }

    [Fact]
    public async Task Query_WithNullTenantId_ReturnsZeroRows()
    {
        // Arrange
        var tenant = Guid.NewGuid();
        var dbName = $"tenanted-null-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).Options;
        var optionsQuery = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).Options;

        var userCtx = TestDbContextFactory.CreateUserContext("user");
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var tenantServiceSeed = TestDbContextFactory.CreateTenantService(tenant);
        var tenantServiceNull = TestDbContextFactory.CreateTenantService(null); // no tenant

        var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDel = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var conc = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();
        var auditQ = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDelQ = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var concQ = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();

        await using var ctxSeed = new TenantedTestDbContext(optionsSeed, audit, softDel, conc, tenantServiceSeed);
        ctxSeed.Database.EnsureCreated();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "SeedEntity", tenant, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        // Act — query with null tenant (should match nothing because TenantId == null matches no Guid)
        await using var ctxQuery = new TenantedTestDbContext(optionsQuery, auditQ, softDelQ, concQ, tenantServiceNull);
        var result = await ctxQuery.TenantedAggregates.ToListAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdForTenantAsync_BypassesFilterAndReturnsByExplicitTenant()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var dbName = $"tenanted-bypass-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).Options;
        var optionsAdmin = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).Options;

        var userCtx = TestDbContextFactory.CreateUserContext("user");
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var tenantServiceSeed = TestDbContextFactory.CreateTenantService(tenant1);
        var tenantServiceAdmin = TestDbContextFactory.CreateTenantService(tenant1); // logged in as tenant1

        var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDel = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var conc = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();
        var auditA = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userCtx, clock);
        var softDelA = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userCtx, clock);
        var concA = new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor();

        await using var ctxSeed = new TenantedTestDbContext(optionsSeed, audit, softDel, conc, tenantServiceSeed);
        ctxSeed.Database.EnsureCreated();
        var id2 = TenantedTestId.New();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "T1", tenant1, new SystemClock()));
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2", tenant2, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        // Create a tenanted repo for tenant1
        await using var ctxAdmin = new TenantedTestDbContext(optionsAdmin, auditA, softDelA, concA, tenantServiceAdmin);
        var repo = new TenantedTestAggregateRepository(ctxAdmin);

        // Act — admin path bypasses filter to fetch tenant2's entity
        var found = await repo.GetByIdForTenantAsync(id2, tenant2);

        // Assert
        found.Should().NotBeNull();
        found!.TenantId.Should().Be(tenant2);
        found.Name.Should().Be("T2");
    }
}

// ---------------------------------------------------------------------------
// Concrete tenanted repository for tests
// ---------------------------------------------------------------------------

internal sealed class TenantedTestAggregateRepository(TenantedTestDbContext ctx)
    : SharedKernel.Persistence.EfCore.MultiTenancy.TenantedRepository<TenantedTestAggregate, TenantedTestId>(ctx);
