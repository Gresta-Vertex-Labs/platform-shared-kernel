using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

/// <summary>
/// The tenant write guard (formerly <c>TenantWriteGuardInterceptor</c>, now part of the merged save pipeline) had NO dedicated unit
/// test coverage before this suite (confirmed by search — only exercised indirectly through
/// pooling/bulk-mutation tests). This closes that gap and covers the exact defect the real-PostgreSQL
/// suite found: <c>SoftDeleteInterceptor</c> setting <c>entry.State</c> DIRECTLY to
/// <see cref="EntityState.Modified"/> makes EF Core flag EVERY scalar property — including
/// <c>TenantId</c>, whose VALUE never actually changed — as <c>IsModified</c>, which used to produce
/// a false-positive "TenantId was changed" rejection on every soft-delete of a tenanted entity. The
/// fix compares actual current-vs-original VALUES instead of the unreliable <c>IsModified</c> flag.
/// </summary>
public sealed class TenantWriteGuardTests
{
    private static TenantedTestDbContext CreateContext(
        SqliteConnection connection, Guid tenantId, out ICrossTenantScope crossTenantScope)
    {
        var options = new DbContextOptionsBuilder<TenantedTestDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId);

        var ctx = new TenantedTestDbContext(
            options,
            PersistenceContextDependencies.Create(actorContext, new SystemClock()));
        ctx.RefreshRequestContext(actorContext);
        crossTenantScope = ctx.CrossTenantScope;
        return ctx;
    }

    [Fact]
    public async Task SaveChanges_TenantIdPropertyValueGenuinelyChanged_Rejected()
    {
        // The false-positive fix compares CurrentValue vs. OriginalValue — this proves a GENUINE
        // TenantId value change (as opposed to a state-flip artifact) is still correctly caught.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var tenantId = Guid.NewGuid();
        using var ctx = CreateContext(connection, tenantId, out _);
        await ctx.Database.EnsureCreatedAsync();

        var entity = new TenantedTestAggregate(TenantedTestId.New(), "Original", tenantId, new SystemClock());
        ctx.TenantedAggregates.Add(entity);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var reloaded = await ctx.TenantedAggregates.FirstAsync(e => e.Id == entity.Id);
        ctx.Entry(reloaded).Property(nameof(SharedKernel.Domain.Abstractions.IHasTenant.TenantId)).CurrentValue = Guid.NewGuid();

        var act = () => ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<ForbiddenException>(
            "a property-level TenantId value change on an otherwise Modified entity must still be rejected");
    }

    [Fact]
    public async Task SaveChanges_SoftDelete_TenantIdValueUnchanged_NotFalsePositive()
    {
        // The regression this test guards: SoftDeleteInterceptor sets entry.State = Modified
        // DIRECTLY, which makes EF Core mark EVERY scalar property IsModified regardless of whether
        // its value changed. Before the fix, this made the guard reject every soft-delete of a
        // tenanted entity with a false "TenantId was changed" error.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SoftDeletableTenantedDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var tenantId = Guid.NewGuid();
        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid(), tenantId);

        using var ctx = new SoftDeletableTenantedDbContext(
            options,
            PersistenceContextDependencies.Create(actorContext, new SystemClock()));
        ctx.RefreshRequestContext(actorContext);
        var crossTenantScope = ctx.CrossTenantScope;
        await ctx.Database.EnsureCreatedAsync();

        var entity = new SoftDeletableTenantedAggregate(TenantedTestId.New(), "ToDelete", tenantId, new SystemClock());
        ctx.SdAggregates.Add(entity);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var toDelete = await ctx.SdAggregates.FirstAsync(e => e.Id == entity.Id);
        ctx.SdAggregates.Remove(toDelete);

        var act = () => ctx.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "soft-deleting a tenanted entity must never be rejected as a false-positive TenantId change");
    }

    [Fact]
    public async Task SaveChanges_DetachedUpdate_AttackerTenantClaimedWithVictimPrimaryKey_Rejected()
    {
        // The INVERTED attack shape: the detached stub claims the ATTACKER's OWN, legitimate current
        // tenant id — passing TenantWriteGuardInterceptor's in-memory "claimed tenant == current
        // tenant" check trivially — but carries a VICTIM row's primary key. The interceptor alone
        // cannot see which tenant the TARGETED ROW actually belongs to; only marking TenantId a
        // concurrency token (TenantedDbContext.ApplyTenantConcurrencyToken) makes the physical UPDATE
        // statement's WHERE clause fail to match the victim's row, surfacing as a translated
        // ForbiddenException instead of silently rewriting it.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var attackerTenant = Guid.NewGuid();
        var victimTenant = Guid.NewGuid();

        using var ctxVictim = CreateContext(connection, victimTenant, out _);
        await ctxVictim.Database.EnsureCreatedAsync();

        var victim = new TenantedTestAggregate(TenantedTestId.New(), "VictimRow", victimTenant, new SystemClock());
        ctxVictim.TenantedAggregates.Add(victim);
        await ctxVictim.SaveChangesAsync();

        using var ctxAttacker = CreateContext(connection, attackerTenant, out _);
        var stub = new TenantedTestAggregate(victim.Id, "HackedFromAttackerTenant", attackerTenant, new SystemClock());
        ctxAttacker.TenantedAggregates.Update(stub);

        var act = () => ctxAttacker.SaveChangesAsync();
        var conflict = (await act.Should().ThrowAsync<ConflictException>(
            "a detached stub carrying the attacker's own tenant id but the victim's primary key must " +
                "still be rejected — answered like a missing row, so the response reveals nothing about another tenant")).Which;
        ConcurrencyVersion.TryGetCurrentVersion(conflict, out _).Should().BeFalse("no version of another tenant's row is disclosed");

        using var ctxVerify = CreateContext(connection, victimTenant, out _);
        var stillThere = await ctxVerify.TenantedAggregates.FirstAsync(e => e.Id == victim.Id);
        stillThere.Name.Should().Be("VictimRow", "the victim's row must be completely untouched");
    }

    [Fact]
    public async Task SaveChanges_DetachedDelete_AttackerTenantClaimedWithVictimPrimaryKey_Rejected()
    {
        // Same inverted shape as the update test above, but via Attach()+Remove() — the delete path
        // EfRepository.DeleteAsync uses for a detached aggregate.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var attackerTenant = Guid.NewGuid();
        var victimTenant = Guid.NewGuid();

        using var ctxVictim = CreateContext(connection, victimTenant, out _);
        await ctxVictim.Database.EnsureCreatedAsync();

        var victim = new TenantedTestAggregate(TenantedTestId.New(), "VictimRow", victimTenant, new SystemClock());
        ctxVictim.TenantedAggregates.Add(victim);
        await ctxVictim.SaveChangesAsync();

        using var ctxAttacker = CreateContext(connection, attackerTenant, out _);
        var stub = new TenantedTestAggregate(victim.Id, "Stub", attackerTenant, new SystemClock());
        ctxAttacker.TenantedAggregates.Attach(stub);
        ctxAttacker.TenantedAggregates.Remove(stub);

        var act = () => ctxAttacker.SaveChangesAsync();
        await act.Should().ThrowAsync<ConflictException>(
            "a detached delete carrying the attacker's own tenant id but the victim's primary key " +
                "must still be rejected, with the same answer as for a row that does not exist");

        using var ctxVerify = CreateContext(connection, victimTenant, out _);
        var stillCount = await ctxVerify.TenantedAggregates.CountAsync(e => e.Id == victim.Id);
        stillCount.Should().Be(1, "the victim's row must survive the rejected delete attempt");
    }

    [Fact]
    public async Task SaveChanges_CrossTenantScopeActive_SkipsEveryCheck()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var ctx = CreateContext(connection, tenantA, out var crossTenantScope);
        await ctx.Database.EnsureCreatedAsync();

        using (crossTenantScope.Enter("test"))
        {
            // Bound to Tenant A, but the entity claims Tenant B — normally rejected, but the
            // explicit bypass is active.
            ctx.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "CrossTenant", tenantB, new SystemClock()));
            var act = () => ctx.SaveChangesAsync();
            await act.Should().NotThrowAsync("an active ICrossTenantScope must skip every tenant-write check");
        }
    }
}
