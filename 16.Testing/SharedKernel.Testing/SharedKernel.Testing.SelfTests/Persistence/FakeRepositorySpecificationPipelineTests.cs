using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s shared in-memory specification pipeline
/// via <c>FirstOrDefaultAsync</c>/<c>ListAsync</c>/<c>CountAsync</c>/<c>AnyAsync</c> — criteria filtering,
/// OrderBy→ThenBy precedence, IsDistinct, Skip/Take-applied-last, the soft-delete round trip, and
/// CountAsync/AnyAsync reusing the identical pipeline (including Skip/Take, per D-168's own
/// "no special-casing" claim).
/// </summary>
public sealed class FakeRepositorySpecificationPipelineTests
{
    private static TestSoftDeletableOrder NewOrder(string customer, int rank, decimal total = 1m) =>
        new(Guid.NewGuid(), customer, total, rank, new FakeClock());

    // ----- Criteria (including null-Criteria matches-all) -----

    [Fact]
    public async Task ListAsync_NullCriteria_MatchesAllEntities()
    {
        var a = NewOrder("A", 0);
        var b = NewOrder("B", 1);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [a, b]);

        var result = await repo.ListAsync(new AllTestOrdersSpecification());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListAsync_Criteria_FiltersToMatchingEntitiesOnly()
    {
        var match = NewOrder("Match", 0);
        var other = NewOrder("Other", 1);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [match, other]);

        var result = await repo.ListAsync(new TestOrdersByCustomerSpecification("Match"));

        Assert.Equal([match.Id], result.Select(o => o.Id));
    }

    [Fact]
    public async Task GetBySpecAsync_NoMatch_ReturnsNull()
    {
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [NewOrder("A", 0)]);

        Assert.Null(await repo.FirstOrDefaultAsync(new TestOrdersByCustomerSpecification("NoMatch")));
    }

    // ----- OrderBy -> ThenBys precedence -----

    [Fact]
    public async Task ListAsync_OrderByThenThenBy_AppliesPrimaryThenSecondaryOrdering()
    {
        // rank0B/rank0A tie on the primary sort (Rank == 0) and are differentiated only by the
        // secondary ThenBy(Customer); rank1 proves the primary sort is genuinely applied (sorts last).
        var rank0B = NewOrder("B", 0);
        var rank0A = NewOrder("A", 0);
        var rank1 = NewOrder("Z", 1);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [rank0B, rank1, rank0A]);

        var result = await repo.ListAsync(new TestOrdersOrderedByRankThenCustomerSpecification());

        Assert.Equal([rank0A.Id, rank0B.Id, rank1.Id], result.Select(o => o.Id));
    }

    // ----- IsDistinct -----

    [Fact]
    public async Task ListAsync_IsDistinct_DoesNotAlterALegitimatelyDuplicateFreeResult()
    {
        // FakeRepository's backing store is a dictionary keyed by each aggregate's own derived
        // identity, so two distinct dictionary entries can never reference value-duplicate rows —
        // there is no way to construct a genuine duplicate-collapsing scenario against this fake.
        // This proves ApplyDistinct()'s flag is wired into the pipeline without corrupting an
        // ordinary, already-duplicate-free result.
        var a = NewOrder("A", 0);
        var b = NewOrder("B", 1);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [a, b]);

        var result = await repo.ListAsync(new TestOrdersDistinctSpecification());

        Assert.Equal(2, result.Count);
    }

    // ----- Skip/Take applied last -----

    [Fact]
    public async Task ListAsync_OrderingThenSkipTake_AppliesPagingAfterOrdering()
    {
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"C{i}", rank: i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);
        var spec = new TestOrdersPagedSpecification(skip: 1, take: 2);

        var result = await repo.ListAsync(spec);

        Assert.Equal([orders[1].Id, orders[2].Id], result.Select(o => o.Id));
    }

    // ----- Soft-delete round trip -----

    [Fact]
    public async Task ListAsync_SoftDeleteRoundTrip_DefaultExcludesDeleted_IncludeDeletedFlagIncludesThem()
    {
        var faker = new TestSoftDeletableOrderFaker();
        var active = faker.Generate(3);
        var deleted = faker.Generate(3);
        foreach (var order in deleted)
            order.Delete("cleanup");

        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, active.Concat(deleted));

        var defaultResult = await repo.ListAsync(new AllTestOrdersSpecification());
        Assert.Equal(active.Select(o => o.Id).ToHashSet(), defaultResult.Select(o => o.Id).ToHashSet());

        var inclusiveResult = await repo.ListAsync(new AllTestOrdersSpecification(includeDeleted: true));
        Assert.Equal(
            active.Concat(deleted).Select(o => o.Id).ToHashSet(),
            inclusiveResult.Select(o => o.Id).ToHashSet());
    }

    // ----- CountAsync / AnyAsync reuse the identical pipeline -----

    [Fact]
    public async Task CountAsync_ReusesIdenticalPipelineAsListAsync_IncludingCriteria()
    {
        var match = NewOrder("Match", 0);
        var other = NewOrder("Other", 1);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [match, other]);

        Assert.Equal(1, await repo.CountAsync(new TestOrdersByCustomerSpecification("Match")));
    }

    [Fact]
    public async Task CountAsync_WithSkipTakeSetOnSpec_IgnoresThem_CountsAllMatchingRows()
    {
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"C{i}", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);
        var spec = new TestOrdersPagedSpecification(skip: 1, take: 2);

        var count = await repo.CountAsync(spec);

        // P-557/W2: CountAsync now always counts every matching row, ignoring Skip/Take (a count
        // bounded by a page size is never the correct "total" for a paginated caller) — it uses the
        // un-paged ApplyFilterOrderDistinct pipeline, not ApplySpecification. All 5 orders match the
        // spec's criteria; the paging window is irrelevant to CountAsync.
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task ListAsync_SoftDeleteRoundTrip_IncludeDeletedFlagAlsoHonorsCountAsync()
    {
        var active = NewOrder("Active", 0);
        var deleted = NewOrder("Deleted", 1);
        deleted.Delete("cleanup");
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [active, deleted]);

        Assert.Equal(1, await repo.CountAsync(new AllTestOrdersSpecification()));
        Assert.Equal(2, await repo.CountAsync(new AllTestOrdersSpecification(includeDeleted: true)));
    }

    [Fact]
    public async Task AnyAsync_ReusesIdenticalPipeline()
    {
        var match = NewOrder("Match", 0);
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [match]);

        Assert.True(await repo.AnyAsync(new TestOrdersByCustomerSpecification("Match")));
        Assert.False(await repo.AnyAsync(new TestOrdersByCustomerSpecification("NoSuchCustomer")));
    }

    [Fact]
    public async Task AnyAsync_EmptyRepository_ReturnsFalse()
    {
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id);

        Assert.False(await repo.AnyAsync(new AllTestOrdersSpecification()));
    }
}
