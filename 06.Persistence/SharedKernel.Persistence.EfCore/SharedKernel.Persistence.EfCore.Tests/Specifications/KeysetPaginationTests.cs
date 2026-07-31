using FluentAssertions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

// ---------------------------------------------------------------------------
// WO-051/P-317 — keyset (cursor/seek) pagination: ISpecificationEvaluator<T>.GetKeysetQuery<TKey>
// and EfReadRepository<TAggregate,TId>.ListKeysetAsync<TKey>.
// WO-051/P-318 — ISpecification<T>.AsSplitQuery propagation (step 2c).
//
// Sort key is `long` (KeysetTestAggregate.SequenceNumber) rather than DateTimeOffset — SQLite's
// EF Core provider does not support ORDER BY over DateTimeOffset columns (a provider-specific test
// limitation, not a defect in the seek-predicate algorithm). The mandatory Id tiebreaker is still
// TestId — a StronglyTypedId<Guid> — so the StronglyTypedId-unwrap fallback in
// SpecificationEvaluator.BuildOrderingComparison is genuinely exercised. PostgreSQL-specific
// DateTimeOffset-keyed coverage lives in SharedKernel.Persistence.PostgreSQL.Tests.
// ---------------------------------------------------------------------------

public sealed class KeysetPaginationTests
{
    private readonly SpecificationEvaluator<KeysetTestAggregate> _evaluator = new();

    private static (TestDbContext Ctx, List<KeysetTestAggregate> Seeded) CreateAndSeedSequentialContext(int count)
    {
        var ctx = TestDbContextFactory.CreateTestDbContext();
        var clock = new SystemClock();

        var seeded = new List<KeysetTestAggregate>();
        for (var i = 0; i < count; i++)
        {
            var aggregate = new KeysetTestAggregate(TestId.New(), $"Item{i}", sequenceNumber: i, clock);
            ctx.KeysetAggregates.Add(aggregate);
            seeded.Add(aggregate);
        }

        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();
        return (ctx, seeded);
    }

    [Fact]
    public async Task GetKeysetQuery_FirstPage_NoAfterKey_ReturnsFirstPageInAscendingOrder()
    {
        // Arrange
        var (ctx, _) = CreateAndSeedSequentialContext(5);
        using var _disposeCtx = ctx;
        var spec = new KeysetBySequenceSpec(afterKey: null, afterId: null, take: 2);

        // Act
        var query = _evaluator.GetKeysetQuery(ctx.KeysetAggregates, spec);
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

        // Assert — the evaluator fetches Take+1 rows (3) so ListKeysetAsync can compute HasMore.
        rows.Should().HaveCount(3);
        rows.Select(a => a.Name).Should().ContainInOrder("Item0", "Item1", "Item2");
    }

    [Fact]
    public async Task ListKeysetAsync_WalksAllPagesInOrder_LastPageHasMoreFalse()
    {
        // Arrange
        var (ctx, _) = CreateAndSeedSequentialContext(5);
        using var _disposeCtx = ctx;
        var repo = new KeysetTestReadRepository(ctx);

        var allItems = new List<string>();
        long? afterKey = null;
        object? afterId = null;
        bool hasMore;
        var pageCount = 0;

        // Act — walk every page via the returned cursor.
        do
        {
            var spec = new KeysetBySequenceSpec(afterKey, afterId, take: 2);
            var page = await repo.ListKeysetAsync(spec);

            allItems.AddRange(page.Items.Select(i => i.Name));
            afterKey = page.NextAfterKey;
            afterId = page.NextAfterId;
            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 10); // safety bound against an infinite loop on a bug

        // Assert
        allItems.Should().ContainInOrder("Item0", "Item1", "Item2", "Item3", "Item4");
        allItems.Should().HaveCount(5);
        pageCount.Should().Be(3); // 2 + 2 + 1
    }

    [Fact]
    public async Task ListKeysetAsync_LastPage_NextAfterKeyAndIdAreNull()
    {
        // Arrange
        var (ctx, _) = CreateAndSeedSequentialContext(3);
        using var _disposeCtx = ctx;
        var repo = new KeysetTestReadRepository(ctx);
        var spec = new KeysetBySequenceSpec(afterKey: null, afterId: null, take: 10);

        // Act — a single page covering everything.
        var page = await repo.ListKeysetAsync(spec);

        // Assert
        page.HasMore.Should().BeFalse();
        page.NextAfterKey.Should().BeNull();
        page.NextAfterId.Should().BeNull();
        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListKeysetAsync_Descending_WalksPagesInDescendingOrder()
    {
        // Arrange
        var (ctx, _) = CreateAndSeedSequentialContext(4);
        using var _disposeCtx = ctx;
        var repo = new KeysetTestReadRepository(ctx);

        var allItems = new List<string>();
        long? afterKey = null;
        object? afterId = null;
        bool hasMore;
        var pageCount = 0;

        do
        {
            var spec = new KeysetBySequenceSpec(afterKey, afterId, take: 2, descending: true);
            var page = await repo.ListKeysetAsync(spec);

            allItems.AddRange(page.Items.Select(i => i.Name));
            afterKey = page.NextAfterKey;
            afterId = page.NextAfterId;
            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 10);

        allItems.Should().ContainInOrder("Item3", "Item2", "Item1", "Item0");
    }

    [Fact]
    public async Task ListAsync_WithKeysetSpecification_SilentlyIgnoresCursor_AlwaysFirstPage()
    {
        // Arrange — hard constraint: passing a KeysetSpecification<T,TKey> to the ordinary
        // ListAsync/GetQuery path compiles and runs but silently ignores AfterKey/AfterId.
        var (ctx, seeded) = CreateAndSeedSequentialContext(5);
        using var _disposeCtx = ctx;
        var repo = new KeysetTestReadRepository(ctx);

        // A spec that, if the cursor were honored, would start from the middle of the set.
        var midCursorSpec = new KeysetBySequenceSpec(
            afterKey: seeded[2].SequenceNumber, afterId: seeded[2].Id, take: 2);

        // Act — via the ORDINARY ListAsync (not ListKeysetAsync).
        var result = await repo.ListAsync(midCursorSpec);

        // Assert — Skip is always 0 on a keyset spec, so ListAsync (ignorant of AfterKey/AfterId)
        // always returns the first page regardless of the supplied cursor.
        result.Select(a => a.Name).Should().ContainInOrder("Item0", "Item1");
    }

    // -----------------------------------------------------------------------
    // WO-051/P-318 — AsSplitQuery propagation (evaluator step 2c)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetQuery_AsSplitQuery_True_AppliesAsSplitQueryToExpressionTree()
    {
        var evaluator = new SpecificationEvaluator<TestAggregate>();
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var spec = new SplitQuerySpec();

        var query = evaluator.GetQuery(ctx.TestAggregates, spec);

        query.Expression.ToString().Should().Contain("AsSplitQuery");
    }

    [Fact]
    public void GetQuery_AsSplitQuery_DefaultFalse_NeverCallsAsSplitQuery()
    {
        var evaluator = new SpecificationEvaluator<TestAggregate>();
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var spec = new PlainSpec();

        var query = evaluator.GetQuery(ctx.TestAggregates, spec);

        query.Expression.ToString().Should().NotContain("AsSplitQuery");
    }

    [Fact]
    public void GetKeysetQuery_AsSplitQuery_True_AppliesAsSplitQueryToExpressionTree()
    {
        var (ctx, _) = CreateAndSeedSequentialContext(1);
        using var _disposeCtx = ctx;
        var spec = new KeysetBySequenceSplitSpec(afterKey: null, afterId: null, take: 2);

        var query = _evaluator.GetKeysetQuery(ctx.KeysetAggregates, spec);

        query.Expression.ToString().Should().Contain("AsSplitQuery");
    }
}

// ---------------------------------------------------------------------------
// Test specification / repository implementations
// ---------------------------------------------------------------------------

internal sealed class KeysetBySequenceSpec : KeysetSpecification<KeysetTestAggregate, long>
{
    public KeysetBySequenceSpec(long? afterKey, object? afterId, int take, bool descending = false)
        : base(a => a.SequenceNumber, a => a.Id, afterKey, afterId, descending, take)
    {
    }
}

internal sealed class KeysetBySequenceSplitSpec : KeysetSpecification<KeysetTestAggregate, long>
{
    public KeysetBySequenceSplitSpec(long? afterKey, object? afterId, int take, bool descending = false)
        : base(a => a.SequenceNumber, a => a.Id, afterKey, afterId, descending, take)
    {
        ApplySplitQuery();
    }
}

internal sealed class SplitQuerySpec : Specification<TestAggregate>
{
    public SplitQuerySpec() => ApplySplitQuery();
}

internal sealed class PlainSpec : Specification<TestAggregate>
{
}

internal sealed class KeysetTestReadRepository(TestDbContext ctx)
    : SharedKernel.Persistence.EfCore.Repositories.EfReadRepository<KeysetTestAggregate, TestId>(
        ctx, new SpecificationEvaluator<KeysetTestAggregate>())
{
}
