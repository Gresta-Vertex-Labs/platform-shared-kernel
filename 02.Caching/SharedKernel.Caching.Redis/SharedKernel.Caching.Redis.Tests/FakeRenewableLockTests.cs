using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Unit tests for <see cref="FakeRenewableLock"/> and <see cref="FakeDistributedLockService"/>
/// from <c>SharedKernel.Testing</c>.
/// </summary>
public sealed class FakeRenewableLockTests
{
    // -------------------------------------------------------------------------
    // FakeRenewableLock — IsAcquired
    // -------------------------------------------------------------------------

    [Fact]
    public void FakeRenewableLock_NewInstance_IsAcquiredTrue()
    {
        var fakeLock = new FakeRenewableLock();
        Assert.True(fakeLock.IsAcquired);
    }

    [Fact]
    public async Task FakeRenewableLock_AfterDispose_IsAcquiredFalse()
    {
        var fakeLock = new FakeRenewableLock();
        await fakeLock.DisposeAsync();
        Assert.False(fakeLock.IsAcquired);
    }

    [Fact]
    public void FakeRenewableLock_SimulateRenewalFailure_IsAcquiredFalse()
    {
        var fakeLock = new FakeRenewableLock { SimulateRenewalFailure = true };
        Assert.False(fakeLock.IsAcquired);
    }

    // -------------------------------------------------------------------------
    // FakeRenewableLock — RenewAsync / RenewalCount
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FakeRenewableLock_RenewAsync_ReturnsTrue_AndIncrementsCount()
    {
        var fakeLock = new FakeRenewableLock();

        var result1 = await fakeLock.RenewAsync();
        var result2 = await fakeLock.RenewAsync();
        var result3 = await fakeLock.RenewAsync();

        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
        Assert.Equal(3, fakeLock.RenewalCount);
    }

    [Fact]
    public async Task FakeRenewableLock_RenewAsync_WhenSimulateFailure_ReturnsFalse_NoCountIncrement()
    {
        var fakeLock = new FakeRenewableLock { SimulateRenewalFailure = true };

        var result = await fakeLock.RenewAsync();

        Assert.False(result);
        Assert.Equal(0, fakeLock.RenewalCount);
    }

    [Fact]
    public async Task FakeRenewableLock_RenewAsync_AfterDispose_ReturnsFalse_NoCountIncrement()
    {
        var fakeLock = new FakeRenewableLock();
        await fakeLock.DisposeAsync();

        var result = await fakeLock.RenewAsync();

        Assert.False(result);
        Assert.Equal(0, fakeLock.RenewalCount);
    }

    [Fact]
    public async Task FakeRenewableLock_DisposeAsync_IsIdempotent()
    {
        var fakeLock = new FakeRenewableLock();

        await fakeLock.DisposeAsync();
        await fakeLock.DisposeAsync(); // second dispose must not throw

        Assert.False(fakeLock.IsAcquired);
    }

    // -------------------------------------------------------------------------
    // FakeDistributedLockService — AcquireAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FakeDistributedLockService_AcquireAsync_ReturnsNonNullHandle()
    {
        var service = new FakeDistributedLockService();

        await using var handle = await service.AcquireAsync(
            "resource:1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task FakeDistributedLockService_AcquireAsync_WhenSimulateFailure_ReturnsNull()
    {
        var service = new FakeDistributedLockService { SimulateFailure = true };

        var handle = await service.AcquireAsync(
            "resource:2", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200));

        Assert.Null(handle);
    }

    // -------------------------------------------------------------------------
    // FakeDistributedLockService — AcquireRenewableAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FakeDistributedLockService_AcquireRenewableAsync_ReturnsFakeRenewableLock()
    {
        var service = new FakeDistributedLockService();

        await using var renewableLock = await service.AcquireRenewableAsync(
            "resource:3", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200));

        Assert.NotNull(renewableLock);
        Assert.IsType<FakeRenewableLock>(renewableLock);
        Assert.True(renewableLock!.IsAcquired);
    }

    [Fact]
    public async Task FakeDistributedLockService_AcquireRenewableAsync_WhenSimulateFailure_ReturnsNull()
    {
        var service = new FakeDistributedLockService { SimulateFailure = true };

        var renewableLock = await service.AcquireRenewableAsync(
            "resource:4", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200));

        Assert.Null(renewableLock);
    }

    [Fact]
    public async Task FakeDistributedLockService_AcquireRenewableAsync_TrackRenewalCount()
    {
        var service = new FakeDistributedLockService();

        await using var renewableLock = await service.AcquireRenewableAsync(
            "resource:5", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(200));

        Assert.NotNull(renewableLock);

        var fakeLock = (FakeRenewableLock)renewableLock!;
        Assert.Equal(0, fakeLock.RenewalCount);

        await renewableLock.RenewAsync();
        await renewableLock.RenewAsync();

        Assert.Equal(2, fakeLock.RenewalCount);
    }

    [Fact]
    public async Task FakeDistributedLockService_AcquireAsync_NullResource_ThrowsArgumentException()
    {
        var service = new FakeDistributedLockService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AcquireAsync("", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task FakeDistributedLockService_AcquireRenewableAsync_NullResource_ThrowsArgumentException()
    {
        var service = new FakeDistributedLockService();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.AcquireRenewableAsync("", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)));
    }
}
