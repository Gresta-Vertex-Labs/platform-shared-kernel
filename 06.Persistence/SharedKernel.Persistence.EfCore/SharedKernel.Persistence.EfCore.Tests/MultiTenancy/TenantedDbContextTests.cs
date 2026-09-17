using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Persistence.EfCore.Tests.MultiTenancy;

public sealed class TenantedDbContextTests
{
    [Fact]
    public async Task Query_WithTenantFilter_ReturnsOnlyCurrentTenantEntities()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var dbName = $"tenanted-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var tenantProvider1 = TestDbContextFactory.CreateTenantProvider(tenant1);
        var tenantProvider2 = TestDbContextFactory.CreateTenantProvider(tenant2);

        var options1 = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var options2 = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctx1 = BuildTenantedContext<TenantedTestDbContext>(options1, userCtx, clock, tenantProvider1);
        ctx1.Database.EnsureCreated();

        var id1 = TenantedTestId.New();
        var id2 = TenantedTestId.New();
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id1, "T1Entity", tenant1, new SystemClock()));
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2Entity", tenant2, new SystemClock()));
        await ctx1.SaveChangesAsync();

        // Act — query with tenant2 filter
        await using var ctx2 = BuildTenantedContext<TenantedTestDbContext>(options2, userCtx, clock, tenantProvider2);
        var tenant2Entities = await ctx2.TenantedAggregates.ToListAsync();

        // Assert
        tenant2Entities.Should().HaveCount(1);
        tenant2Entities[0].TenantId.Should().Be(tenant2);
        tenant2Entities[0].Name.Should().Be("T2Entity");
    }

    [Fact]
    public async Task Query_WithGuidEmptyTenantProvider_ReturnsZeroRows()
    {
        // Arrange — P-092: the default tenant provider returns Guid.Empty → filter matches no rows
        var tenant = Guid.NewGuid();
        var dbName = $"tenanted-empty-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var seedProvider = TestDbContextFactory.CreateTenantProvider(tenant);
        var emptyProvider = TestDbContextFactory.CreateTenantProvider(Guid.Empty);

        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var optionsQuery = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctxSeed = BuildTenantedContext<TenantedTestDbContext>(optionsSeed, userCtx, clock, seedProvider);
        ctxSeed.Database.EnsureCreated();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "SeedEntity", tenant, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        // Act — query with Guid.Empty provider (no-op / no real tenant)
        await using var ctxQuery = BuildTenantedContext<TenantedTestDbContext>(optionsQuery, userCtx, clock, emptyProvider);
        var result = await ctxQuery.TenantedAggregates.ToListAsync();

        // Assert — zero rows: Guid.Empty matches no production entity
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

        var provider1 = TestDbContextFactory.CreateTenantProvider(tenant1);
        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var optionsAdmin = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctxSeed = BuildTenantedContext<TenantedTestDbContext>(optionsSeed, userCtx, clock, provider1);
        ctxSeed.Database.EnsureCreated();
        var id2 = TenantedTestId.New();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "T1", tenant1, new SystemClock()));
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2", tenant2, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        await using var ctxAdmin = BuildTenantedContext<TenantedTestDbContext>(optionsAdmin, userCtx, clock, provider1);
        var repo = new TenantedTestAggregateRepository(ctxAdmin);

        // Act — admin path bypasses filter to fetch tenant2's entity
        var found = await repo.GetByIdForTenantAsync(id2, tenant2);

        // Assert
        found.Should().NotBeNull();
        found!.TenantId.Should().Be(tenant2);
        found.Name.Should().Be("T2");
    }

    // Helper to build TenantedTestDbContext with resolved interceptors.
    private static TenantedTestDbContext BuildTenantedContext<TCtx>(
        DbContextOptions<TenantedTestDbContext> options,
        IUserContext userCtx,
        IClock clock,
        ITenantProvider tenantProvider)
    {
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();
        var audit = new AuditInterceptor(userCtx, clock, svcOpts);
        var softDel = new SoftDeleteInterceptor(userCtx, clock, svcOpts);
        var conc = new ConcurrencyInterceptor();
        return new TenantedTestDbContext(options, audit, softDel, conc, tenantProvider);
    }
}

// ---------------------------------------------------------------------------
// Concrete tenanted repository for tests
// ---------------------------------------------------------------------------

internal sealed class TenantedTestAggregateRepository(TenantedTestDbContext ctx)
    : SharedKernel.Persistence.EfCore.MultiTenancy.TenantedRepository<TenantedTestAggregate, TenantedTestId>(ctx);
