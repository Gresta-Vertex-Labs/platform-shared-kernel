using FluentAssertions;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Specifications;

// ---------------------------------------------------------------------------
// Keyset (cursor/seek) pagination: ISpecificationEvaluator<T>.GetKeysetQuery<TKey>
// and EfReadRepository<TAggregate,TId>.ListKeysetAsync<TKey>.
// ISpecification<T>.AsSplitQuery propagation (step 2c).
//
// Sort key is `long` (KeysetTestAggregate.SequenceNumber) rather than DateTimeOffset — SQLite's
// EF Core provider does not support ORDER BY over DateTimeOffset columns (a provider-specific test
// limitation, not a defect in the seek-predicate algorithm). The mandatory Id tiebreaker is still
// TestId — a StronglyTypedId<Guid> — so the StronglyTypedId-unwrap fallback in
// SpecificationEvaluator.BuildOrderingComparison is genuinely exercised. PostgreSQL-specific
// DateTimeOffset-keyed coverage lives in SharedKernel.Persistence.EfCore.Tests.
// ---------------------------------------------------------------------------

public sealed class KeysetPaginationTests
{
    private readonly SpecificationEvaluator<KeysetTestAggregate> _evaluator = new();

    // ListKeysetAsync now returns CursorPagedList<T> (04.Contracts) — NextAfterKey/NextAfterId
    // are gone in favor of one opaque NextCursor string. Decode it back to (key, id) to build the next
    // page's spec, mirroring how a real caller would round-trip a wire cursor.
    private static (long? Key, object? Id) DecodeCursor(string? cursor)
    {
        if (cursor is null)
            return (null, null);

        var decoded = PageCursor.Decode<long, TestId>(cursor);
        decoded.IsSuccess.Should().BeTrue("the cursor this evaluator produced must always decode successfully");
        return (decoded.Value.Key, decoded.Value.Id);
    }

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
            (afterKey, afterId) = DecodeCursor(page.NextCursor);
            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 10); // safety bound against an infinite loop on a bug

        // Assert
        allItems.Should().ContainInOrder("Item0", "Item1", "Item2", "Item3", "Item4");
        allItems.Should().HaveCount(5);
        pageCount.Should().Be(3); // 2 + 2 + 1
    }

    [Fact]
    public async Task GetKeysetQuery_SecondPage_DirectEvaluatorCall_ContinuesSequence_NoGapOrOverlap()
    {
        // Arrange — exercises SpecificationEvaluator.GetKeysetQuery<TKey> directly (not via
        // ListKeysetAsync/the repository) for both the first AND second page, per T-66's own wording.
        var (ctx, _) = CreateAndSeedSequentialContext(5);
        using var _disposeCtx = ctx;

        var firstPageSpec = new KeysetBySequenceSpec(afterKey: null, afterId: null, take: 2);
        var firstPageQuery = _evaluator.GetKeysetQuery(ctx.KeysetAggregates, firstPageSpec);
        var firstPageRows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(firstPageQuery);
        var lastOfFirstPage = firstPageRows.Take(2).Last();

        // Act — second page, cursor anchored to the last row actually returned on page 1.
        var secondPageSpec = new KeysetBySequenceSpec(
            afterKey: lastOfFirstPage.SequenceNumber, afterId: lastOfFirstPage.Id, take: 2);
        var secondPageQuery = _evaluator.GetKeysetQuery(ctx.KeysetAggregates, secondPageSpec);
        var secondPageRows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(secondPageQuery);

        // Assert — continues immediately after Item1 with no gap/overlap (evaluator fetches Take+1).
        secondPageRows.Select(a => a.Name).Should().ContainInOrder("Item2", "Item3", "Item4");
    }

    [Fact]
    public async Task ListKeysetAsync_ConcurrentInsertsBetweenPageFetches_NoDuplicatesOrSkips()
    {
        // Arrange — seed rows with deliberately spaced sequence numbers so an inserted row can land
        // strictly BETWEEN two already-seeded values without violating uniqueness.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var clock = new SystemClock();

        var seq10 = new KeysetTestAggregate(TestId.New(), "Seq10", 10, clock);
        var seq20 = new KeysetTestAggregate(TestId.New(), "Seq20", 20, clock);
        var seq30 = new KeysetTestAggregate(TestId.New(), "Seq30", 30, clock);
        var seq40 = new KeysetTestAggregate(TestId.New(), "Seq40", 40, clock);
        ctx.KeysetAggregates.AddRange(seq10, seq20, seq30, seq40);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        var repo = new KeysetTestReadRepository(ctx);

        // Act — fetch page 1 (take 2): Seq10, Seq20.
        var page1 = await repo.ListKeysetAsync(new KeysetBySequenceSpec(afterKey: null, afterId: null, take: 2));
        page1.Items.Select(i => i.Name).Should().ContainInOrder("Seq10", "Seq20");

        // Simulate a write racing between page fetches: a new row with a sequence number that would
        // have shifted an OFFSET-based page 2 boundary (it lands BEFORE the cursor, between the two
        // already-returned rows) — the exact scenario OFFSET pagination cannot handle correctly.
        var seq15 = new KeysetTestAggregate(TestId.New(), "Seq15", 15, clock);
        ctx.KeysetAggregates.Add(seq15);
        ctx.SaveChanges();
        ctx.ChangeTracker.Clear();

        // Act — fetch page 2 using the CURSOR returned by page 1 (anchored to Seq20's key/id, not a
        // numeric offset that the intervening insert would have invalidated).
        var (page1AfterKey, page1AfterId) = DecodeCursor(page1.NextCursor);
        var page2 = await repo.ListKeysetAsync(
            new KeysetBySequenceSpec(afterKey: page1AfterKey, afterId: page1AfterId, take: 2));

        // Assert — page 2 continues strictly after the cursor: Seq30, Seq40. The newly-inserted
        // Seq15 (which sorts BEFORE the cursor) never reappears, and nothing from page 1 is
        // duplicated — the correctness property offset pagination lacks under concurrent writes.
        page2.Items.Select(i => i.Name).Should().ContainInOrder("Seq30", "Seq40");
        page2.Items.Should().NotContain(i => i.Name == "Seq15",
            "a row inserted BEFORE the cursor must never reappear in a later keyset page");
        page2.Items.Should().NotContain(i => i.Name == "Seq10" || i.Name == "Seq20",
            "keyset pagination must never duplicate rows already returned in an earlier page");
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
        page.NextCursor.Should().BeNull();
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
            (afterKey, afterId) = DecodeCursor(page.NextCursor);
            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 10);

        allItems.Should().ContainInOrder("Item3", "Item2", "Item1", "Item0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListKeysetAsync_DuplicateSortKeys_ReturnsEveryRowExactlyOnce(bool descending)
    {
        // Several rows share each sort key, so page boundaries fall inside a run of equal keys and only
        // the Id tiebreak decides what comes next. Regression guard: the tiebreak used to sort ascending
        // while the seek predicate compared Ids descending, so a descending walk skipped or repeated rows.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var clock = new SystemClock();
        long[] keys = [0, 1, 1, 1, 2, 2, 2, 2, 3];
        for (var i = 0; i < keys.Length; i++)
            ctx.KeysetAggregates.Add(new KeysetTestAggregate(TestId.New(), $"Item{i}", keys[i], clock));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repo = new KeysetTestReadRepository(ctx);

        var seen = new List<KeysetTestAggregate>();
        long? afterKey = null;
        object? afterId = null;
        bool hasMore;
        var pageCount = 0;
        do
        {
            var page = await repo.ListKeysetAsync(new KeysetBySequenceSpec(afterKey, afterId, take: 2, descending));
            seen.AddRange(page.Items);
            (afterKey, afterId) = DecodeCursor(page.NextCursor);
            hasMore = page.HasMore;
            pageCount++;
        } while (hasMore && pageCount < 20);

        seen.Select(a => a.Name).Should().OnlyHaveUniqueItems().And.HaveCount(keys.Length);
        var sequence = seen.Select(a => a.SequenceNumber).ToList();
        (descending ? sequence.Should().BeInDescendingOrder() : sequence.Should().BeInAscendingOrder()).Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_WithKeysetSpecification_ThrowsInsteadOfSilentlyIgnoringCursor()
    {
        // Passing a KeysetSpecification<T,TKey> to the
        // ordinary ListAsync/GetQuery path used to compile and run, silently ignoring AfterKey/AfterId
        // and always returning the first page — a real, previously-undetectable bug class. It now
        // throws loudly instead.
        var (ctx, seeded) = CreateAndSeedSequentialContext(5);
        using var _disposeCtx = ctx;
        var repo = new KeysetTestReadRepository(ctx);

        var midCursorSpec = new KeysetBySequenceSpec(
            afterKey: seeded[2].SequenceNumber, afterId: seeded[2].Id, take: 2);

        // Act — via the ORDINARY ListAsync (not ListKeysetAsync).
        var act = async () => await repo.ListAsync(midCursorSpec);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ListKeysetAsync*");
    }

    // -----------------------------------------------------------------------
    // AsSplitQuery propagation (evaluator step 2c)
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
