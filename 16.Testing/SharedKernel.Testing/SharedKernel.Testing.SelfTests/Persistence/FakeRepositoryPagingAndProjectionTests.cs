using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Testing.SelfTests.Persistence;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s paging/projection/streaming/id-lookup
/// surface: <c>ListPagedAsync</c>, <c>ListProjectedAsync</c>, <c>GetBySpecProjectedAsync</c>,
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

    // ----- ListPagedAsync: multi-page paging arithmetic -----

    [Fact]
    public async Task ListPagedAsync_FirstPage_ReturnsExpectedWindowAndTotalCount()
    {
        var repo = RepositoryWithOrders(10);

        var page = await repo.ListPagedAsync(new TestOrdersPagedSpecification(skip: 0, take: 3));

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(10, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_MiddlePage_ReturnsExpectedWindow()
    {
        var repo = RepositoryWithOrders(10);

        var page = await repo.ListPagedAsync(new TestOrdersPagedSpecification(skip: 3, take: 3));

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(10, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_FinalPartialPage_ReturnsRemainderOnly()
    {
        var repo = RepositoryWithOrders(10);

        var page = await repo.ListPagedAsync(new TestOrdersPagedSpecification(skip: 9, take: 3));

        Assert.Single(page.Items);
        Assert.Equal(10, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_EmptyResult_ReturnsZeroTotalCountAndNoItems()
    {
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id);

        var page = await repo.ListPagedAsync(new TestOrdersPagedSpecification(skip: 0, take: 10));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task ListPagedAsync_SpecWithNoTakeSet_IsTreatedAsOneFullPage()
    {
        var repo = RepositoryWithOrders(5);

        var page = await repo.ListPagedAsync(new TestOrdersUnpagedOrderedSpecification());

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(5, page.PageSize);
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
    public async Task GetBySpecProjectedAsync_Match_ReturnsProjectedResult()
    {
        var repo = RepositoryWithOrders(3);
        var spec = new TestOrdersCustomerProjectionSpecification(o => o.Customer == "C1");

        Assert.Equal("C1", await repo.GetBySpecProjectedAsync(spec));
    }

    [Fact]
    public async Task GetBySpecProjectedAsync_NoMatch_ReturnsDefault()
    {
        var repo = RepositoryWithOrders(3);
        var spec = new TestOrdersCustomerProjectionSpecification(o => o.Customer == "NoSuchCustomer");

        Assert.Null(await repo.GetBySpecProjectedAsync(spec));
    }

    [Fact]
    public async Task ListPagedProjectedAsync_SameDerivationAsListPagedAsync_WithProjectionAppliedLast()
    {
        var repo = RepositoryWithOrders(10);
        var spec = new TestOrdersPagedCustomerProjectionSpecification(skip: 2, take: 3);

        var page = await repo.ListPagedProjectedAsync(spec);

        Assert.Equal(["C2", "C3", "C4"], page.Items);
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

    // ----- GetByIdsChunkedAsync: identical results to the unchunked call -----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(100)] // larger than the id count
    public async Task GetByIdsChunkedAsync_ProducesIdenticalResultsToTheUnchunkedCall(int chunkSize)
    {
        var repo = RepositoryWithOrders(7);
        var ids = repo.Items.Keys.ToList();

        var unchunked = await repo.GetByIdsAsync(ids);
        var chunked = await repo.GetByIdsChunkedAsync(ids, chunkSize);

        Assert.Equal(unchunked.Select(o => o.Id).ToHashSet(), chunked.Select(o => o.Id).ToHashSet());
        Assert.Equal(unchunked.Count, chunked.Count);
    }

    [Fact]
    public async Task GetByIdsChunkedAsync_ChunkSizeLessThanOne_ThrowsArgumentOutOfRangeException()
    {
        var repo = RepositoryWithOrders(3);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repo.GetByIdsChunkedAsync(repo.Items.Keys, chunkSize: 0));
    }
}
