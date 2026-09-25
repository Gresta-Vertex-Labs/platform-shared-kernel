using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeDistributedLockService"/> and <see cref="FakeDistributedLock"/> against the
/// <see cref="IDistributedLockService"/> contract: exclusivity, fencing, failure simulation and lease expiry.
/// </summary>
public sealed class FakeDistributedLockServiceTests
{
    [Fact]
    public async Task TryAcquireAsync_HeldResource_SecondAcquisitionReturnsNull()
    {
        var service = new FakeDistributedLockService();

        await using var first = await service.TryAcquireAsync("resource");
        var second = await service.TryAcquireAsync("resource");

        Assert.NotNull(first);
        Assert.True(first.IsHeld);
        Assert.Equal("resource", first.Resource);
        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentResources_DoNotContend()
    {
        var service = new FakeDistributedLockService();

        await using var a = await service.TryAcquireAsync("a");
        await using var b = await service.TryAcquireAsync("b");

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public async Task FencingToken_IncreasesPerResource_AcrossLocksAndLeases()
    {
        var service = new FakeDistributedLockService();

        var first = await service.TryAcquireAsync("resource");
        await first!.DisposeAsync();
        var second = await service.TryAcquireAsync("resource");
        await second!.DisposeAsync();
        var other = await service.TryAcquireAsync("other");
        var lease = await service.TryAcquireLeaseAsync("resource", TimeSpan.FromMinutes(1));

        Assert.Equal(1, first.FencingToken);
        Assert.Equal(2, second.FencingToken);
        Assert.Equal(1, other!.FencingToken);
        Assert.Equal(3, lease!.FencingToken);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesResource_ForNextAcquirer()
    {
        var service = new FakeDistributedLockService();

        var first = (FakeDistributedLock)(await service.TryAcquireAsync("resource"))!;
        await first.DisposeAsync();
        await using var second = await service.TryAcquireAsync("resource");

        Assert.True(first.IsReleased);
        Assert.False(first.IsHeld);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task SimulateContention_EveryAcquisitionReturnsNull()
    {
        var service = new FakeDistributedLockService { SimulateContention = true };

        Assert.Null(await service.TryAcquireAsync("resource"));
        Assert.Null(await service.TryAcquireLeaseAsync("resource", TimeSpan.FromMinutes(1)));
        Assert.Empty(service.AcquiredLocks);
        Assert.Empty(service.AcquiredLeases);
    }

    [Fact]
    public async Task SimulateUnavailable_EveryAcquisitionThrows()
    {
        var service = new FakeDistributedLockService { SimulateUnavailable = true };

        var lockEx = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            async () => await service.TryAcquireAsync("resource"));
        await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            async () => await service.TryAcquireLeaseAsync("resource", TimeSpan.FromMinutes(1)));

        Assert.Equal("resource", lockEx.Resource);
    }

    [Fact]
    public async Task Lease_BlocksLockAndLeaseUntilExpiry_ThenFreesResource()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = new FakeDistributedLockService(time);

        var lease = await service.TryAcquireLeaseAsync("resource", TimeSpan.FromSeconds(30));

        Assert.NotNull(lease);
        Assert.Equal(time.GetUtcNow() + TimeSpan.FromSeconds(30), lease.ExpiresAt);
        Assert.Null(await service.TryAcquireAsync("resource"));
        Assert.Null(await service.TryAcquireLeaseAsync("resource", TimeSpan.FromSeconds(30)));

        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await service.TryAcquireAsync("resource"));

        time.Advance(TimeSpan.FromSeconds(1));
        await using var afterExpiry = await service.TryAcquireAsync("resource");
        Assert.NotNull(afterExpiry);
    }

    [Fact]
    public async Task HeldLock_BlocksLease()
    {
        var service = new FakeDistributedLockService();

        await using var held = await service.TryAcquireAsync("resource");

        Assert.Null(await service.TryAcquireLeaseAsync("resource", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task SimulateLoss_CancelsLostToken_AndFreesResource()
    {
        var service = new FakeDistributedLockService();
        var handle = (FakeDistributedLock)(await service.TryAcquireAsync("resource"))!;

        handle.SimulateLoss();

        Assert.False(handle.IsHeld);
        Assert.False(handle.IsReleased);
        Assert.True(handle.LostToken.IsCancellationRequested);
        await using var next = await service.TryAcquireAsync("resource");
        Assert.NotNull(next);
    }

    [Fact]
    public async Task SimulateLoss_ThenDispose_DoesNotReleaseNewHoldersLock()
    {
        var service = new FakeDistributedLockService();
        var lost = (FakeDistributedLock)(await service.TryAcquireAsync("resource"))!;
        lost.SimulateLoss();
        await using var current = await service.TryAcquireAsync("resource");

        await lost.DisposeAsync();

        Assert.NotNull(current);
        Assert.Null(await service.TryAcquireAsync("resource"));
    }

    [Fact]
    public async Task AcquiredLocksAndLeases_RecordEveryHandOutInOrder()
    {
        var service = new FakeDistributedLockService();

        var a = await service.TryAcquireAsync("a");
        var b = await service.TryAcquireAsync("b");
        var lease = await service.TryAcquireLeaseAsync("c", TimeSpan.FromMinutes(1));
        _ = await service.TryAcquireAsync("a");

        Assert.Equal([a!, b!], service.AcquiredLocks.Cast<IDistributedLock>());
        Assert.Equal([lease!], service.AcquiredLeases);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_NonPositiveDuration_Throws()
    {
        var service = new FakeDistributedLockService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await service.TryAcquireLeaseAsync("resource", TimeSpan.Zero));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
