using SharedKernel.Application.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

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

        // TenantedDbContext takes a separate IRequestContext — tenant identity no
        // longer flows through the same seam as actor identity.
        var actorContext1 = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenant1);
        var actorContext2 = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenant2);

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

        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctx1 = BuildTenantedContext(options1, actorContext1, clock);
        ctx1.Database.EnsureCreated();

        var id1 = TenantedTestId.New();
        var id2 = TenantedTestId.New();
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id1, "T1Entity", tenant1, new SystemClock()));
        ctx1.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2Entity", tenant2, new SystemClock()));
        await ctx1.SaveChangesAsync();

        // Act — query with tenant2 filter
        await using var ctx2 = BuildTenantedContext(options2, actorContext2, clock);
        var tenant2Entities = await ctx2.TenantedAggregates.ToListAsync();

        // Assert
        tenant2Entities.Should().HaveCount(1);
        tenant2Entities[0].TenantId.Should().Be(tenant2);
        tenant2Entities[0].Name.Should().Be("T2Entity");
    }

    [Fact]
    public async Task Query_WithNoTenantResolved_ReturnsZeroRows()
    {
        // Arrange — fail-closed: a null (unresolved) tenant matches no rows, replacing
        // the former Guid.Empty-sentinel test — Guid.Empty is now an ordinary, matchable tenant id.
        var tenant = Guid.NewGuid();
        var dbName = $"tenanted-empty-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var seedActorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenant);
        var noTenantActorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId: null);

        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var optionsQuery = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr).EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctxSeed = BuildTenantedContext(optionsSeed, seedActorContext, clock);
        ctxSeed.Database.EnsureCreated();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "SeedEntity", tenant, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        // Act — query with no tenant resolved at all.
        await using var ctxQuery = BuildTenantedContext(optionsQuery, noTenantActorContext, clock);
        var result = await ctxQuery.TenantedAggregates.ToListAsync();

        // Assert — zero rows: a null tenant matches no production entity.
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdForTenantAsync_WithoutActiveCrossTenantScope_Throws()
    {
        // The cross-tenant escape hatch must be explicit and attributable.
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctx = BuildTenantedContext(options, actorContext, clock);
        ctx.Database.EnsureCreated();
        var repo = new TenantedTestAggregateRepository(ctx, new CrossTenantScope());

        var act = async () => await repo.GetByIdForTenantAsync(TenantedTestId.New(), Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ICrossTenantScope*");
    }

    [Fact]
    public async Task GetByIdForTenantAsync_BypassesFilterAndReturnsByExplicitTenant()
    {
        // Arrange
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var dbName = $"tenanted-bypass-{Guid.NewGuid():N}";
        var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        var actorContext1 = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenant1);
        var optionsSeed = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        var optionsAdmin = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connStr)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        await using var ctxSeed = BuildTenantedContext(optionsSeed, actorContext1, clock);
        ctxSeed.Database.EnsureCreated();
        var id2 = TenantedTestId.New();
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "T1", tenant1, new SystemClock()));
        ctxSeed.TenantedAggregates.Add(new TenantedTestAggregate(id2, "T2", tenant2, new SystemClock()));
        await ctxSeed.SaveChangesAsync();

        await using var ctxAdmin = BuildTenantedContext(optionsAdmin, actorContext1, clock);
        var crossTenantScope = new CrossTenantScope();
        var repo = new TenantedTestAggregateRepository(ctxAdmin, crossTenantScope);

        // Act — admin path, under an explicit cross-tenant scope, bypasses the filter to fetch
        // tenant2's entity.
        TenantedTestAggregate? found;
        using (crossTenantScope.Enter())
        {
            found = await repo.GetByIdForTenantAsync(id2, tenant2);
        }

        // Assert
        found.Should().NotBeNull();
        found!.TenantId.Should().Be(tenant2);
        found.Name.Should().Be("T2");
    }

    // Helper to build TenantedTestDbContext with resolved interceptors. TenantedDbContext
    // takes a separate IRequestContext — FakeAuditActorContext implements both
    // IRequestContext and IRequestContext on one object, so the same fake serves both
    // roles here, exactly as before the seam split.
    private static TenantedTestDbContext BuildTenantedContext(
        DbContextOptions<TenantedTestDbContext> options,
        FakeAuditActorContext actorContext,
        IClock clock)
    {
        var audit = new AuditInterceptor(actorContext, clock);
        var softDel = new SoftDeleteInterceptor(clock);
        var conc = new ConcurrencyInterceptor();
        var ctx = new TenantedTestDbContext(options, new PersistenceContextDependencies(audit, softDel, conc));
        ctx.RefreshRequestContext(actorContext);
        return ctx;
    }
}

// ---------------------------------------------------------------------------
// Concrete tenanted repository for tests
// ---------------------------------------------------------------------------

internal sealed class TenantedTestAggregateRepository(TenantedTestDbContext ctx, ICrossTenantScope crossTenantScope)
    : SharedKernel.Persistence.EfCore.MultiTenancy.TenantedRepository<TenantedTestAggregate, TenantedTestId>(ctx, crossTenantScope);
