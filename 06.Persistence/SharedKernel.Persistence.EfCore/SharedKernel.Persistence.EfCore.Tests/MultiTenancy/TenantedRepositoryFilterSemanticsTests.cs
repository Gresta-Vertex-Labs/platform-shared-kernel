using FluentAssertions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.MultiTenancy;

// ---------------------------------------------------------------------------
// Concrete TenantedRepository implementations for tests
// ---------------------------------------------------------------------------

internal sealed class SdTenantedRepo(SoftDeletableTenantedDbContext ctx, ICrossTenantScope crossTenantScope)
    : TenantedRepository<SoftDeletableTenantedAggregate, TenantedTestId>(ctx, crossTenantScope);

// ---------------------------------------------------------------------------
// T-28 — TenantedRepository soft-delete filter semantics
// ---------------------------------------------------------------------------

public sealed class TenantedRepositoryFilterSemanticsTests
{
    // -----------------------------------------------------------------------
    // Test 1: GetByIdForTenantAsync excludes soft-deleted records
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetByIdForTenantAsync_ReturnsNull_ForSoftDeletedEntity()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var ctx = TestDbContextFactory.CreateSoftDeletableTenantedDbContext(tenantId);
        var crossTenantScope = new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance);
        var repo = new SdTenantedRepo(ctx, crossTenantScope);

        var id = TenantedTestId.New();
        var entity = new SoftDeletableTenantedAggregate(id, "ToSoftDelete", tenantId, new SystemClock());
        await ctx.SdAggregates.AddAsync(entity);
        await ctx.SaveChangesAsync();

        // Soft-delete: remove the already-tracked entity (no need to reload via FindAsync)
        ctx.SdAggregates.Remove(entity);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act
        using var scope1 = crossTenantScope.Enter("test");
        var result = await repo.GetByIdForTenantAsync(id, tenantId);

        // Assert
        result.Should().BeNull(
            "GetByIdForTenantAsync preserves the soft-delete filter — " +
            "soft-deleted records must be excluded");
    }

    // -----------------------------------------------------------------------
    // Test 1b: GetByIdForTenantIncludingDeletedAsync returns soft-deleted entity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetByIdForTenantIncludingDeletedAsync_Returns_SoftDeletedEntity()
    {
        var tenantId = Guid.NewGuid();
        using var ctx = TestDbContextFactory.CreateSoftDeletableTenantedDbContext(tenantId);
        var crossTenantScope = new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance);
        var repo = new SdTenantedRepo(ctx, crossTenantScope);

        var id = TenantedTestId.New();
        var entity = new SoftDeletableTenantedAggregate(id, "IncludeDeleted", tenantId, new SystemClock());
        await ctx.SdAggregates.AddAsync(entity);
        await ctx.SaveChangesAsync();

        // Soft-delete: remove the already-tracked entity
        ctx.SdAggregates.Remove(entity);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act — both filters bypassed
        using var scope1 = crossTenantScope.Enter("test");
        var result = await repo.GetByIdForTenantIncludingDeletedAsync(id, tenantId);

        // Assert
        result.Should().NotBeNull(
            "GetByIdForTenantIncludingDeletedAsync bypasses BOTH the tenant AND soft-delete filters");
    }

    // -----------------------------------------------------------------------
    // Test 2: GetByIdForTenantAsync respects tenant boundary for live records
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetByIdForTenantAsync_Returns_LiveEntity_ForCorrectTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var ctx = TestDbContextFactory.CreateSoftDeletableTenantedDbContext(tenantA);
        var crossTenantScope = new CrossTenantScope(SharedKernel.Application.Context.AnonymousRequestContext.Instance);
        var repo = new SdTenantedRepo(ctx, crossTenantScope);
        using var scope1 = crossTenantScope.Enter("test");

        var id = TenantedTestId.New();
        await ctx.SdAggregates.AddAsync(
            new SoftDeletableTenantedAggregate(id, "TenantA_Entity", tenantA, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Correct tenant → entity returned
        var found = await repo.GetByIdForTenantAsync(id, tenantA);
        found.Should().NotBeNull("entity belongs to tenantA and is not soft-deleted");

        // Wrong tenant → null
        var notFound = await repo.GetByIdForTenantAsync(id, tenantB);
        notFound.Should().BeNull("tenantB cannot see tenantA's data");
    }

    // -----------------------------------------------------------------------
    // Test 3: Non-ISoftDeletable type check — static assertion
    // -----------------------------------------------------------------------

    [Fact]
    public void TenantedTestAggregate_IsNot_ISoftDeletable()
    {
        // Non-ISoftDeletable entities: both GetByIdForTenantAsync and
        // GetByIdForTenantIncludingDeletedAsync behave identically.
        typeof(ISoftDeletable)
            .IsAssignableFrom(typeof(TenantedTestAggregate))
                .Should().BeFalse("TenantedTestAggregate is not soft-deletable — both tenant-lookup methods are equivalent");
    }

    [Fact]
    public void SoftDeletableTenantedAggregate_Is_ISoftDeletable()
    {
        typeof(ISoftDeletable)
            .IsAssignableFrom(typeof(SoftDeletableTenantedAggregate))
                .Should().BeTrue("SoftDeletableTenantedAggregate implements ISoftDeletable");
    }
}
