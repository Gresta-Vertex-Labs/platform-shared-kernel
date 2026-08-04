using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s <c>ListKeysetAsync&lt;TKey&gt;</c> — first-page
/// traversal, a full multi-page walk with no loss/duplication, the mandatory id-tiebreaker under a
/// tied sort key, both ascending and descending directions, and a criteria-filtered keyset walk.
/// </summary>
public sealed class FakeRepositoryKeysetTests
{
    private static TestSoftDeletableOrder NewOrder(string customer, int rank) =>
        new(Guid.NewGuid(), customer, 1m, rank, new FakeClock());

    private static async Task<List<Guid>> WalkAllPagesAsync(
        FakeRepository<TestSoftDeletableOrder, Guid> repo,
        Func<int?, object?, int, TestOrdersByRankKeysetSpecification> specFactory,
        int take)
    {
        var visited = new List<Guid>();
        int? afterKey = null;
        object? afterId = null;
        bool hasMore;
        do
        {
            var page = await repo.ListKeysetAsync(specFactory(afterKey, afterId, take));
            visited.AddRange(page.Items.Select(o => o.Id));
            afterKey = page.NextAfterKey;
            afterId = page.NextAfterId;
            hasMore = page.HasMore;
        } while (hasMore);

        return visited;
    }

    [Fact]
    public async Task ListKeysetAsync_FirstPage_NoCursor_ReturnsFirstTakeRowsInOrder()
    {
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"C{i}", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);
        var spec = new TestOrdersByRankKeysetSpecification(afterKey: null, afterId: null, descending: false, take: 2);

        var page = await repo.ListKeysetAsync(spec);

        Assert.Equal([orders[0].Id, orders[1].Id], page.Items.Select(o => o.Id));
        Assert.True(page.HasMore);
        Assert.Equal(1, page.NextAfterKey!.Value);
        Assert.Equal(orders[1].Id, (Guid)page.NextAfterId!);
    }

    [Fact]
    public async Task ListKeysetAsync_FullWalk_VisitsEveryRowExactlyOnce_TerminatesWithHasMoreFalse()
    {
        var orders = Enumerable.Range(0, 7).Select(i => NewOrder($"C{i}", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);

        var visited = await WalkAllPagesAsync(
            repo,
            (afterKey, afterId, take) => new TestOrdersByRankKeysetSpecification(afterKey, afterId, descending: false, take),
            take: 3);

        Assert.Equal(orders.Select(o => o.Id), visited); // no skip, no duplicate, in ascending order
    }

    [Fact]
    public async Task ListKeysetAsync_DescendingWalk_VisitsEveryRowInReverseOrder()
    {
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"C{i}", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);

        var visited = await WalkAllPagesAsync(
            repo,
            (afterKey, afterId, take) => new TestOrdersByRankKeysetSpecification(afterKey, afterId, descending: true, take),
            take: 2);

        Assert.Equal(orders.Reverse().Select(o => o.Id), visited);
    }

    [Fact]
    public async Task ListKeysetAsync_TiedSortKeyValues_IdTiebreakerPagesDeterministicallyWithNoLossOrDuplication()
    {
        // All three orders share Rank == 0 — only the mandatory Id tiebreaker (always applied
        // ascending, regardless of the primary sort direction) can determine a stable page order.
        var tied = Enumerable.Range(0, 3)
            .Select(_ => NewOrder("Same", rank: 0))
            .OrderBy(o => o.Id) // the tiebreaker's own expected resulting order
            .ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, tied);

        var visited = await WalkAllPagesAsync(
            repo,
            (afterKey, afterId, take) => new TestOrdersByRankKeysetSpecification(afterKey, afterId, descending: false, take),
            take: 1);

        Assert.Equal(tied.Select(o => o.Id), visited);
    }

    [Fact]
    public async Task ListKeysetAsync_CriteriaFilteredWalk_ComposesFilterAndSeekPredicate()
    {
        var included = Enumerable.Range(0, 4).Select(i => NewOrder("Keep", rank: i)).ToArray();
        var excluded = Enumerable.Range(0, 4).Select(i => NewOrder("Drop", rank: i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, included.Concat(excluded));

        var visited = new List<Guid>();
        int? afterKey = null;
        object? afterId = null;
        bool hasMore;
        do
        {
            var spec = new TestOrdersByRankKeysetWithCriteriaSpecification(
                afterKey, afterId, descending: false, take: 2, customer: "Keep");
            var page = await repo.ListKeysetAsync(spec);
            visited.AddRange(page.Items.Select(o => o.Id));
            afterKey = page.NextAfterKey;
            afterId = page.NextAfterId;
            hasMore = page.HasMore;
        } while (hasMore);

        Assert.Equal(included.Select(o => o.Id).ToHashSet(), visited.ToHashSet());
        Assert.Equal(included.Length, visited.Count); // none of the "Drop" rows ever appeared
    }
}
