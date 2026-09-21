using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Persistence;
using SharedKernel.Persistence.Testing;

namespace SharedKernel.Persistence.Testing.Tests.Fakes;

/// <summary>
/// Proves <see cref="FakeRepository{TAggregate, TId}"/>'s write-side (<c>IRepository&lt;TAggregate,TId&gt;</c>)
/// members: Add/Update/Delete (single and range), Exists/GetById, <c>SimulateFailure</c>'s
/// write-path-only scope, and <c>Seed</c>/<c>Reset</c>.
/// </summary>
public sealed class FakeRepositoryTests
{
    private static FakeRepository<TestSoftDeletableOrder, Guid> CreateRepository(
        IEnumerable<TestSoftDeletableOrder>? seed = null) =>
        new(o => o.Id, seed);

    private static TestSoftDeletableOrder NewOrder(string customer = "Ada", decimal total = 100m, int rank = 0) =>
        new(Guid.NewGuid(), customer, total, rank, new FakeClock());

    // ----- AddAsync -----

    [Fact]
    public async Task AddAsync_NewAggregate_IsRetrievableByGetByIdAsync()
    {
        var repo = CreateRepository();
        var order = NewOrder();

        await repo.AddAsync(order);

        Assert.Same(order, await repo.GetByIdAsync(order.Id));
    }

    [Fact]
    public async Task AddAsync_DuplicateKey_ThrowsInvalidOperationException()
    {
        var order = NewOrder();
        var repo = CreateRepository([order]);
        var duplicate = new TestSoftDeletableOrder(order.Id, "Other", 1m, 0, new FakeClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAsync(duplicate));
    }

    // ----- UpdateAsync -----

    [Fact]
    public async Task UpdateAsync_ExistingAggregate_ReplacesStoredValue()
    {
        var order = NewOrder(total: 10m);
        var repo = CreateRepository([order]);
        var updated = new TestSoftDeletableOrder(order.Id, order.Customer, 999m, order.Rank, new FakeClock());

        await repo.UpdateAsync(updated);

        var found = await repo.GetByIdAsync(order.Id);
        Assert.Equal(999m, found!.Total);
    }

    [Fact]
    public async Task UpdateAsync_MissingKey_ThrowsKeyNotFoundException()
    {
        var repo = CreateRepository();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.UpdateAsync(NewOrder()));
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_MissingKey_IsIdempotentNoOp()
    {
        var repo = CreateRepository();

        await repo.DeleteAsync(NewOrder()); // must not throw

        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletableAggregateAlreadyFlaggedDeleted_IsStillHardRemoved()
    {
        var order = NewOrder();
        order.Delete("someone");
        var repo = CreateRepository([order]);

        await repo.DeleteAsync(order);

        // Proves DeleteAsync ALWAYS hard-removes — the aggregate is truly gone from .Items, not
        // merely flagged (it was already IsDeleted == true before this call).
        Assert.False(repo.Items.ContainsKey(order.Id));
        Assert.Empty(repo.Items);
    }

    // ----- Range members: per-item, sequential, non-atomic -----

    [Fact]
    public async Task AddRangeAsync_SequentialNonAtomic_AppliesEarlierItemsBeforeStoppingAtFailure()
    {
        var existing = NewOrder(customer: "Existing");
        var repo = CreateRepository([existing]);
        var newOrderA = NewOrder(customer: "A");
        var duplicateOfExisting = new TestSoftDeletableOrder(existing.Id, "Dup", 1m, 0, new FakeClock());
        var neverReached = NewOrder(customer: "Never");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AddRangeAsync([newOrderA, duplicateOfExisting, neverReached]));

        Assert.True(repo.Items.ContainsKey(newOrderA.Id));      // processed before the failure
        Assert.False(repo.Items.ContainsKey(neverReached.Id));  // never reached
    }

    [Fact]
    public async Task UpdateRangeAsync_SequentialNonAtomic_AppliesEarlierItemsBeforeStoppingAtFailure()
    {
        var orderA = NewOrder(customer: "A", total: 1m);
        var missingOrderId = Guid.NewGuid();
        var repo = CreateRepository([orderA]);
        var updatedA = new TestSoftDeletableOrder(orderA.Id, orderA.Customer, 42m, orderA.Rank, new FakeClock());
        var updateForMissing = new TestSoftDeletableOrder(missingOrderId, "Missing", 1m, 0, new FakeClock());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => repo.UpdateRangeAsync([updatedA, updateForMissing]));

        var reloaded = await repo.GetByIdAsync(orderA.Id);
        Assert.Equal(42m, reloaded!.Total); // first item's update DID apply before the failure
    }

    [Fact]
    public async Task DeleteRangeAsync_SequentialNonAtomic_EachItemIndividuallyIdempotent()
    {
        var orderA = NewOrder();
        var orderB = NewOrder();
        var neverSeeded = NewOrder();
        var repo = CreateRepository([orderA, orderB]);

        await repo.DeleteRangeAsync([orderA, neverSeeded, orderB]); // idempotent — never throws

        Assert.Empty(repo.Items);
    }

    // ----- Id lookups hide soft-deleted rows, like production -----

    [Fact]
    public async Task ExistsAsync_And_GetByIdAsync_HideSoftDeletedRows()
    {
        var order = NewOrder();
        order.Delete("someone");
        var repo = CreateRepository([order]);

        Assert.False(await repo.ExistsAsync(order.Id));
        Assert.Null(await repo.GetByIdAsync(order.Id));
        Assert.True(repo.Items.ContainsKey(order.Id));
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        var repo = CreateRepository();

        Assert.False(await repo.ExistsAsync(Guid.NewGuid()));
    }

    // ----- SimulateFailure: write-path-only scope -----

    [Fact]
    public async Task SimulateFailure_GatesAllSixMutatingMembers()
    {
        var order = NewOrder();
        var repo = CreateRepository([order]);
        repo.SimulateFailure = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAsync(NewOrder()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpdateAsync(order));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAsync(order));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddRangeAsync([NewOrder()]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpdateRangeAsync([order]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteRangeAsync([order]));
    }

    [Fact]
    public async Task SimulateFailure_DoesNotAffectTheThreePureLookupMembers()
    {
        var order = NewOrder();
        var repo = CreateRepository([order]);
        repo.SimulateFailure = true;

        Assert.NotNull(await repo.GetByIdAsync(order.Id));
        Assert.True(await repo.ExistsAsync(order.Id));
        Assert.NotNull(await repo.FirstOrDefaultAsync(new AllTestOrdersSpecification()));
    }

    // ----- Seed / Reset -----

    [Fact]
    public void Seed_BypassesSimulateFailureAndTheDuplicateGuard()
    {
        var order = NewOrder();
        var repo = CreateRepository();
        repo.SimulateFailure = true;

        repo.Seed([order]);
        repo.Seed([order]); // re-seeding the same key overwrites — never throws, unlike AddAsync

        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task Reset_ClearsItemsOnly_NeverTouchesSimulateFailure()
    {
        var order = NewOrder();
        var repo = CreateRepository([order]);
        repo.SimulateFailure = true;

        repo.Reset();

        Assert.Empty(repo.Items);
        Assert.True(repo.SimulateFailure); // Reset does not reset this flag
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AddAsync(NewOrder()));
    }
}
