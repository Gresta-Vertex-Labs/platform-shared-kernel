using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Repositories;

// ---------------------------------------------------------------------------
// Concrete repository implementations for bulk mutation tests (C-85, C-86)
// ---------------------------------------------------------------------------

internal sealed class BulkTestRepository(TestDbContext ctx)
    : EfRepository<TestAggregate, TestId>(ctx);

internal sealed class BulkAuditableRepository(TestDbContext ctx)
    : EfRepository<AuditableTestAggregate, TestId>(ctx);

internal sealed class BulkSdTenantedRepository(SoftDeletableTenantedDbContext ctx)
    : EfRepository<SoftDeletableTenantedAggregate, TenantedTestId>(ctx);

internal sealed class BulkConcurrentRepository(TestDbContext ctx)
    : EfRepository<ConcurrentTestAggregate, TestId>(ctx);

// ---------------------------------------------------------------------------
// Helper specifications
// ---------------------------------------------------------------------------

internal sealed class NameEqualsSpec : Specification<TestAggregate>
{
    public NameEqualsSpec(string name) => AddCriteria(e => e.Name == name);
}

internal sealed class NoCriteriaSpec : Specification<TestAggregate>
{
}

internal sealed class SdTenantedNameEqualsSpec : Specification<SoftDeletableTenantedAggregate>
{
    public SdTenantedNameEqualsSpec(string name) => AddCriteria(e => e.Name == name);
}

internal sealed class ConcurrentNameEqualsSpec : Specification<ConcurrentTestAggregate>
{
    public ConcurrentNameEqualsSpec(string name) => AddCriteria(e => e.Name == name);
}

internal sealed class AuditableNameEqualsSpec : Specification<AuditableTestAggregate>
{
    public AuditableNameEqualsSpec(string name) => AddCriteria(e => e.Name == name);

    public AuditableNameEqualsSpec(string name, bool includeDeleted)
        : this(name)
    {
        if (includeDeleted)
            IncludeSoftDeleted();
    }
}

internal sealed class IncludesSpec : Specification<TestAggregate>
{
    public IncludesSpec() => AddInclude(e => e.Name);
}

internal sealed class StringIncludesSpec : Specification<TestAggregate>
{
    public StringIncludesSpec() => AddStringInclude("SomeNavigation");
}

internal sealed class OrderBySpec : Specification<TestAggregate>
{
    public OrderBySpec() => ApplyOrderBy(e => e.Name);
}

internal sealed class OrderByDescendingSpec : Specification<TestAggregate>
{
    public OrderByDescendingSpec() => ApplyOrderByDescending(e => e.Name);
}

internal sealed class ThenBySpec : Specification<TestAggregate>
{
    // Deliberately omits ApplyOrderBy/ApplyOrderByDescending so the guard's "ThenBys" check
    // (rather than its "Ordering" check) is the one that fires.
    public ThenBySpec() => ApplyThenBy(e => e.Id.Value, descending: false);
}

internal sealed class SkipTakeSpec : Specification<TestAggregate>
{
    public SkipTakeSpec() => ApplyPaging(skip: 1, take: 2);
}

// ---------------------------------------------------------------------------
// IBulkMutationRepository.ExecuteUpdateAsync / ExecuteDeleteAsync tests (C-85)
// ---------------------------------------------------------------------------

public sealed class BulkMutationRepositoryTests
{
    [Fact]
    public async Task ExecuteUpdateAsync_CriteriaOnlySpec_UpdatesOnlyMatchedRows()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);
        ctx.TestAggregates.AddRange(
            new TestAggregate(TestId.New(), "Match", new SystemClock()),
            new TestAggregate(TestId.New(), "Match", new SystemClock()),
            new TestAggregate(TestId.New(), "NoMatch", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var affected = await repo.ExecuteUpdateAsync(
            new NameEqualsSpec("Match"),
            setters => setters.SetProperty(e => e.Name, "Updated"));

        affected.Should().Be(2);

        var names = await ctx.TestAggregates.Select(e => e.Name).ToListAsync();
        names.Should().Contain("Updated").And.HaveCount(3);
        names.Count(n => n == "Updated").Should().Be(2);
        names.Should().Contain("NoMatch");
    }

    [Fact]
    public async Task ExecuteDeleteAsync_PhysicallyRemovesMatchedRows_EvenForSoftDeletable()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);
        ctx.AuditableAggregates.AddRange(
            new AuditableTestAggregate(TestId.New(), "DeleteMe", new SystemClock()),
            new AuditableTestAggregate(TestId.New(), "DeleteMe", new SystemClock()),
            new AuditableTestAggregate(TestId.New(), "Keep", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var affected = await repo.ExecuteDeleteAsync(new AuditableNameEqualsSpec("DeleteMe"));

        affected.Should().Be(2);

        // Physical delete — not even visible with IgnoreQueryFilters.
        var remaining = await ctx.AuditableAggregates.IgnoreQueryFilters().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Name.Should().Be("Keep");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_DoesNotInvokeAuditInterceptor_UnlessExplicitlySet()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);
        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "Original", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var beforeModifiedOn = (await ctx.AuditableAggregates
            .IgnoreQueryFilters()
            .Select(e => new { e.Id, e.ModifiedOn })
            .FirstAsync(e => e.Id == id)).ModifiedOn;

        var affected = await repo.ExecuteUpdateAsync(
            new AuditableNameEqualsSpec("Original"),
            setters => setters.SetProperty(e => e.Name, "BulkUpdated"));

        affected.Should().Be(1);

        var afterModifiedOn = (await ctx.AuditableAggregates
            .IgnoreQueryFilters()
            .Select(e => new { e.Id, e.ModifiedOn })
            .FirstAsync(e => e.Id == id)).ModifiedOn;

        afterModifiedOn.Should().Be(beforeModifiedOn, "AuditInterceptor must not run for bulk mutations");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_IncludeDeletedTrue_AppliesIgnoreQueryFilters_MatchesSoftDeletedRows()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);
        var id = TestId.New();
        ctx.AuditableAggregates.Add(new AuditableTestAggregate(id, "SoftDeleted", new SystemClock()));
        await ctx.SaveChangesAsync();

        // Soft-delete it
        var toDelete = await ctx.AuditableAggregates.FindAsync(id);
        ctx.AuditableAggregates.Remove(toDelete!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Without IncludeDeleted — global filter excludes the row, no match.
        var affectedWithoutFlag = await repo.ExecuteUpdateAsync(
            new AuditableNameEqualsSpec("SoftDeleted"),
            setters => setters.SetProperty(e => e.Name, "ShouldNotApply"));
        affectedWithoutFlag.Should().Be(0);

        // With IncludeDeleted — IgnoreQueryFilters applied, row matched.
        var affectedWithFlag = await repo.ExecuteUpdateAsync(
            new AuditableNameEqualsSpec("SoftDeleted", includeDeleted: true),
            setters => setters.SetProperty(e => e.Name, "Rotated"));
        affectedWithFlag.Should().Be(1);

        var updated = await ctx.AuditableAggregates.IgnoreQueryFilters().FirstAsync(e => e.Id == id);
        updated.Name.Should().Be("Rotated");
    }

    // -------------------------------------------------------------------------
    // Bulk restore is a USAGE PATTERN of the already-shipped
    // ExecuteUpdateAsync, not a new production method. This proves the documented pattern
    // (06.Persistence/CLAUDE.md's "Soft-Delete Restore" section) genuinely works: a bulk
    // soft-delete followed by a bulk restore, both via ExecuteUpdateAsync, bypassing
    // SaveChangesAsync/the three platform interceptors/domain events exactly like every other
    // IBulkMutationRepository.ExecuteUpdateAsync call.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteUpdateAsync_BulkRestorePattern_ReversesBulkSoftDelete_BypassingInterceptors()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkAuditableRepository(ctx);

        var id1 = TestId.New();
        var id2 = TestId.New();
        ctx.AuditableAggregates.AddRange(
            new AuditableTestAggregate(id1, "BulkRestoreMe", new SystemClock()),
            new AuditableTestAggregate(id2, "BulkRestoreMe", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var originalModifiedOn = (await ctx.AuditableAggregates
            .IgnoreQueryFilters()
            .Select(e => new { e.Id, e.ModifiedOn })
            .FirstAsync(e => e.Id == id1)).ModifiedOn;

        // Bulk soft-delete via ExecuteUpdateAsync directly (mirrors the documented bulk
        // soft-delete example — no repository-level "bulk delete" helper exists; the setter
        // delegate itself IS the documented pattern).
        var deletedCount = await repo.ExecuteUpdateAsync(
            new AuditableNameEqualsSpec("BulkRestoreMe"),
            setters => setters
                .SetProperty(e => ((ISoftDeletable)e).IsDeleted, true)
                .SetProperty(e => ((ISoftDeletable)e).DeletedOn, (DateTimeOffset?)DateTimeOffset.UtcNow)
                .SetProperty(e => ((ISoftDeletable)e).DeletedBy, "bulk-tester"));
        deletedCount.Should().Be(2);

        // Invisible through the normal global soft-delete filter.
        var visibleAfterDelete = await ctx.AuditableAggregates.Where(e => e.Name == "BulkRestoreMe").ToListAsync();
        visibleAfterDelete.Should().BeEmpty();

        // Act — bulk RESTORE via a second ExecuteUpdateAsync call. IncludeDeleted = true so the
        // soft-deleted rows are matched (mirrors 06.Persistence/CLAUDE.md's documented example).
        var restoredCount = await repo.ExecuteUpdateAsync(
            new AuditableNameEqualsSpec("BulkRestoreMe", includeDeleted: true),
            setters => setters
                .SetProperty(e => ((ISoftDeletable)e).IsDeleted, false)
                .SetProperty(e => ((ISoftDeletable)e).DeletedOn, (DateTimeOffset?)null)
                .SetProperty(e => ((ISoftDeletable)e).DeletedBy, (string?)null));

        // Assert — every targeted row is genuinely restored and visible again through the global
        // filter with no IncludeDeleted flag needed.
        restoredCount.Should().Be(2);

        var visibleAfterRestore = await ctx.AuditableAggregates.Where(e => e.Name == "BulkRestoreMe").ToListAsync();
        visibleAfterRestore.Should().HaveCount(2);
        visibleAfterRestore.Should().OnlyContain(e => !e.IsDeleted && e.DeletedOn == null && e.DeletedBy == null);

        // Bypasses SaveChangesAsync/the three platform interceptors/domain events — ModifiedOn is
        // untouched by either bulk call, exactly like every other IBulkMutationRepository call.
        var finalModifiedOn = (await ctx.AuditableAggregates
            .Select(e => new { e.Id, e.ModifiedOn })
                .FirstAsync(e => e.Id == id1)).ModifiedOn;
        finalModifiedOn.Should().Be(originalModifiedOn, "bulk mutations must bypass AuditInterceptor entirely");
    }
}

// ---------------------------------------------------------------------------
// BulkSpecificationGuard / UnsupportedSpecificationException tests (C-86)
// ---------------------------------------------------------------------------

public sealed class BulkSpecificationGuardTests
{
    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithIncludes_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new IncludesSpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("Includes");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithStringIncludes_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new StringIncludesSpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("StringIncludes");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithOrderBy_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new OrderBySpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("Ordering");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithOrderByDescending_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new OrderByDescendingSpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("Ordering");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithThenBys_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new ThenBySpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("ThenBys");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SpecWithSkipTake_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new SkipTakeSpec(),
            setters => setters.SetProperty(e => e.Name, "X"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("Paging");
    }

    [Fact]
    public async Task ExecuteDeleteAsync_SpecWithIncludes_ThrowsUnsupportedSpecificationException()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteDeleteAsync(new IncludesSpec());

        await act.Should().ThrowAsync<UnsupportedSpecificationException>();
    }

    [Fact]
    public async Task ExecuteUpdateAsync_CriteriaOnlySpec_DoesNotThrow()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new NameEqualsSpec("Match"),
            setters => setters.SetProperty(e => e.Name, "Unchanged"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteUpdateAsync_AllRowsSpecification_DoesNotThrow()
    {
        // A criteria-less bulk mutation is rejected UNLESS the caller explicitly
        // opts in via AllRowsSpecification<T>.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new AllRowsSpecification<TestAggregate>(),
            setters => setters.SetProperty(e => e.Name, "Unchanged"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteUpdateAsync_NoCriteriaAndNotAllRows_ThrowsUnsupportedSpecificationException()
    {
        // The previously-accepted "just don't set Criteria" shape is now rejected —
        // callers wanting every row must say so explicitly via AllRowsSpecification<T>.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new NoCriteriaSpec(),
            setters => setters.SetProperty(e => e.Name, "Unchanged"));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("Criteria").And.Contain(nameof(AllRowsSpecification<TestAggregate>));
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SetsTenantId_ThrowsUnsupportedSpecificationException()
    {
        // TenantId must never be settable via a bulk mutation — it would bypass
        // TenantWriteGuardInterceptor entirely.
        using var ctx = TestDbContextFactory.CreateSoftDeletableTenantedDbContext();
        var repo = new BulkSdTenantedRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new SdTenantedNameEqualsSpec("Match"),
            setters => setters.SetProperty(e => e.TenantId, Guid.NewGuid()));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("TenantId");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_SetsRowVersion_ThrowsUnsupportedSpecificationException()
    {
        // The concurrency token must never be forgeable via a bulk mutation.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkConcurrentRepository(ctx);

        var act = async () => await repo.ExecuteUpdateAsync(
            new ConcurrentNameEqualsSpec("Match"),
            setters => setters.SetProperty(e => e.RowVersion, [1, 2, 3, 4]));

        var exception = await act.Should().ThrowAsync<UnsupportedSpecificationException>();
        exception.Which.Message.Should().Contain("RowVersion");
    }

    [Fact]
    public async Task ExecuteUpdateAsync_GuardViolation_NoSqlIssued_NoRowsAffected()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var repo = new BulkTestRepository(ctx);
        ctx.TestAggregates.Add(new TestAggregate(TestId.New(), "Untouched", new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await repo.ExecuteUpdateAsync(
            new OrderBySpec(),
            setters => setters.SetProperty(e => e.Name, "ShouldNotApply"));

        await act.Should().ThrowAsync<UnsupportedSpecificationException>();

        var unchanged = await ctx.TestAggregates.Select(e => e.Name).ToListAsync();
        unchanged.Should().ContainSingle().Which.Should().Be("Untouched");
    }
}
