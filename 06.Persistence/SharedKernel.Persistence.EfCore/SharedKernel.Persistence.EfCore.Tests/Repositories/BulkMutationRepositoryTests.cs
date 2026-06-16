using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

// ---------------------------------------------------------------------------
// Helper specifications
// ---------------------------------------------------------------------------

internal sealed class NameEqualsSpec : Specification<TestAggregate>
{
    public NameEqualsSpec(string name) => AddCriteria(e => e.Name == name);
}

internal sealed class AllAggregatesSpec : Specification<TestAggregate>
{
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
            new AllAggregatesSpec(),
            setters => setters.SetProperty(e => e.Name, "Unchanged"));

        await act.Should().NotThrowAsync();
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
