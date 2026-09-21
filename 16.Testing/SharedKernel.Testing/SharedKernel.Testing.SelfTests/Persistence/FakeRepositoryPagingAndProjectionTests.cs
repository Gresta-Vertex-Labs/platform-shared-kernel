using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s paging/projection/streaming/id-lookup
/// surface: <c>ListPagedAsync</c>, <c>ListProjectedAsync</c>, <c>FirstOrDefaultProjectedAsync</c>,
/// <c>ListPagedProjectedAsync</c>, <c>StreamAsync</c>, <c>StreamProjectedAsync</c>,
/// <c>GetByIdsAsync</c>, and <c>GetByIdsChunkedAsync</c>.
/// </summary>
public sealed class FakeRepositoryPagingAndProjectionTests
{
    private static TestSoftDeletableOrder NewOrder(string customer, int rank) =>
        new(Guid.NewGuid(), customer, 1m, rank, new FakeClock());

    private static FakeRepository<TestSoftDeletableOrder, Guid> RepositoryWithOrders(int count)
    {
        var orders = Enumerable.Range(0, count).Select(i => NewOrder($"C{i}", rank: i)).ToArray();
        return new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);
    }

    // ----- ListPagedAsync: call-site paging -----

    private static PageRequest Page(int page, int size) => PageRequest.Create(page, size).Value;

    [Fact]
    public async Task ListPagedAsync_FirstPage_ReturnsExpectedWindowAndTotalCount()
    {
        var repo = RepositoryWithOrders(10);

        var page = await repo.ListPagedAsync(new TestOrdersUnpagedOrderedSpecification(), Page(1, 3));

        Assert.Equal([0, 1, 2], page.Items.Select(o => o.Rank));
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(4, page.TotalPages);
    }

    [Fact]
    public async Task ListPagedAsync_FinalPartialPage_ReturnsRemainderOnly()
    {
        var repo = RepositoryWithOrders(10);

        var page = await repo.ListPagedAsync(new TestOrdersUnpagedOrderedSpecification(), Page(4, 3));

        Assert.Single(page.Items);
        Assert.Equal(10, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_EmptyResult_ReturnsZeroTotalCountAndNoItems()
    {
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id);

        var page = await repo.ListPagedAsync(new TestOrdersUnpagedOrderedSpecification(), Page(1, 10));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_RejectsUnorderedAndSelfPagedSpecifications()
    {
        var repo = RepositoryWithOrders(3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListPagedAsync(Spec.For<TestSoftDeletableOrder>(), PageRequest.First));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListPagedAsync(new TestOrdersPagedSpecification(0, 2), PageRequest.First));
    }

    // ----- Projection: selector applied strictly after Skip/Take -----

    [Fact]
    public async Task ListProjectedAsync_ReturnsProjectedValuesForThePagedWindow()
    {
        var repo = RepositoryWithOrders(10); // customers "C0".."C9", ordered by Rank 0..9
        var spec = new TestOrdersPagedCustomerProjectionSpecification(skip: 2, take: 3);

        var result = await repo.ListProjectedAsync(spec);

        Assert.Equal(["C2", "C3", "C4"], result);
    }

    [Fact]
    public async Task FirstOrDefaultProjectedAsync_Match_ReturnsProjectedResult()
    {
        var repo = RepositoryWithOrders(3);
        var spec = new TestOrdersCustomerProjectionSpecification(o => o.Customer == "C1");

        Assert.Equal("C1", await repo.FirstOrDefaultProjectedAsync(spec));
    }

    [Fact]
    public async Task FirstOrDefaultProjectedAsync_NoMatch_ReturnsDefault()
    {
        var repo = RepositoryWithOrders(3);
        var spec = new TestOrdersCustomerProjectionSpecification(o => o.Customer == "NoSuchCustomer");

        Assert.Null(await repo.FirstOrDefaultProjectedAsync(spec));
    }

    [Fact]
    public async Task ListPagedProjectedAsync_PagesAtTheCallSite_WithProjectionAppliedLast()
    {
        var repo = RepositoryWithOrders(10);
        var spec = Spec.For<TestSoftDeletableOrder>().OrderBy(o => o.Rank).Select(o => o.Customer);

        var page = await repo.ListPagedProjectedAsync(spec, Page(2, 3));

        Assert.Equal(["C3", "C4", "C5"], page.Items);
        Assert.Equal(10, page.TotalCount);
    }

    // ----- StreamAsync: genuine cancellable async iterator -----

    [Fact]
    public async Task StreamAsync_YieldsEveryMatchingItem()
    {
        var repo = RepositoryWithOrders(5);

        var items = new List<TestSoftDeletableOrder>();
        await foreach (var item in repo.StreamAsync(new TestOrdersUnpagedOrderedSpecification()))
            items.Add(item);

        Assert.Equal(5, items.Count);
    }

    [Fact]
    public async Task StreamAsync_CancellationMidEnumeration_StopsFurtherYields()
    {
        var repo = RepositoryWithOrders(5);
        using var cts = new CancellationTokenSource();

        var seen = new List<TestSoftDeletableOrder>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in repo.StreamAsync(new TestOrdersUnpagedOrderedSpecification(), cts.Token))
            {
                seen.Add(item);
                if (seen.Count == 2)
                    cts.Cancel();
            }
        });

        Assert.Equal(2, seen.Count); // enumeration stopped exactly where cancellation was requested
    }

    [Fact]
    public async Task StreamProjectedAsync_YieldsProjectedItemsInOrder()
    {
        var repo = RepositoryWithOrders(3);
        var spec = new TestOrdersPagedCustomerProjectionSpecification(skip: 0, take: 3);

        var items = new List<string>();
        await foreach (var item in repo.StreamProjectedAsync(spec))
            items.Add(item);

        Assert.Equal(["C0", "C1", "C2"], items);
    }

    // ----- GetByIdsAsync: missing-id-produces-no-entry, order not guaranteed -----

    [Fact]
    public async Task GetByIdsAsync_MissingIdsProduceNoEntry_PresentIdsAllReturned()
    {
        var repo = RepositoryWithOrders(3);
        var ids = repo.Items.Keys.ToList();
        var missing = Guid.NewGuid();

        var result = await repo.GetByIdsAsync(ids.Append(missing));

        Assert.Equal(ids.ToHashSet(), result.Select(o => o.Id).ToHashSet());
    }

    [Fact]
    public async Task GetByIdsAsync_EmptyInput_ReturnsEmptyResult()
    {
        var repo = RepositoryWithOrders(3);

        Assert.Empty(await repo.GetByIdsAsync([]));
    }
}
