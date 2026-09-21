using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.Testing;

namespace SharedKernel.Persistence.Testing.Tests.Fakes;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s call-site keyset paging: first page, a full walk
/// with no loss or duplication under tied keys, both directions, the specification filter, and the same
/// rejections as production.
/// </summary>
public sealed class FakeRepositoryKeysetTests
{
    private static TestSoftDeletableOrder NewOrder(string customer, int rank) =>
        new(Guid.NewGuid(), customer, 1m, rank, new FakeClock());

    private static async Task<List<TestSoftDeletableOrder>> WalkAsync(
        FakeRepository<TestSoftDeletableOrder, Guid> repo,
        ISpecification<TestSoftDeletableOrder> spec,
        bool descending,
        int limit)
    {
        var visited = new List<TestSoftDeletableOrder>();
        string? cursor = null;
        do
        {
            var page = await repo.ListKeysetAsync(spec, CursorPageRequest.Create(cursor, limit).Value, o => o.Rank, descending);
            visited.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return visited;
    }

    [Fact]
    public async Task FirstPage_ReturnsTheLowestKeys_AndACursor()
    {
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"C{i}", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);

        var page = await repo.ListKeysetAsync(Spec.For<TestSoftDeletableOrder>(), CursorPageRequest.Create(limit: 2).Value, o => o.Rank);

        Assert.Equal([orders[0].Id, orders[1].Id], page.Items.Select(o => o.Id));
        Assert.True(page.HasMore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullWalk_WithTiedKeys_VisitsEveryRowExactlyOnce_InOrder(bool descending)
    {
        var orders = Enumerable.Range(0, 7).Select(i => NewOrder($"C{i}", i / 2)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);

        var visited = await WalkAsync(repo, Spec.For<TestSoftDeletableOrder>(), descending, limit: 3);

        Assert.Equal(orders.Length, visited.Select(o => o.Id).Distinct().Count());
        Assert.Equal(orders.Length, visited.Count);
        var ranks = visited.Select(o => o.Rank).ToList();
        Assert.Equal(descending ? ranks.OrderByDescending(r => r) : ranks.OrderBy(r => r), ranks);
    }

    [Fact]
    public async Task Walk_AppliesTheSpecificationFilter()
    {
        var orders = Enumerable.Range(0, 6).Select(i => NewOrder(i % 2 == 0 ? "Even" : "Odd", i)).ToArray();
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, orders);

        var visited = await WalkAsync(repo, Spec.For<TestSoftDeletableOrder>().Where(o => o.Customer == "Even"), false, limit: 2);

        Assert.Equal([0, 2, 4], visited.Select(o => o.Rank));
    }

    [Fact]
    public async Task RejectsOrderedSpecifications_AndMalformedCursors()
    {
        var repo = new FakeRepository<TestSoftDeletableOrder, Guid>(o => o.Id, [NewOrder("C", 1)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ListKeysetAsync(
            Spec.For<TestSoftDeletableOrder>().OrderBy(o => o.Customer), CursorPageRequest.First, o => o.Rank));
        await Assert.ThrowsAsync<ValidationException>(() => repo.ListKeysetAsync(
            Spec.For<TestSoftDeletableOrder>(), CursorPageRequest.Create("v1.garbage").Value, o => o.Rank));
    }
}
