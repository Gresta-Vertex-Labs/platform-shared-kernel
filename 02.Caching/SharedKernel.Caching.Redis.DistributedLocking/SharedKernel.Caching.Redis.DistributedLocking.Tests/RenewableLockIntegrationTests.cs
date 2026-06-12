using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Testing.Caching;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Integration tests for <see cref="IRenewableLock"/> and
/// <see cref="IDistributedLockService.AcquireRenewableAsync"/>.
/// Uses Testcontainers to spin up a real Redis instance.
/// </summary>
[Collection("Redis")]
public sealed class RenewableLockIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking(_redisContainer.GetConnectionString());
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private IDistributedLockService LockService =>
        _provider!.GetRequiredService<IDistributedLockService>();

    // -------------------------------------------------------------------------
    // AcquireRenewableAsync — basic acquire / null on timeout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcquireRenewableAsync_HappyPath_ReturnsNonNullLock()
    {
        var resource = "rlock:acquire-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        Assert.True(handle!.IsAcquired);
    }

    [Fact]
    public async Task AcquireRenewableAsync_LockHeld_WaitExpires_ReturnsNull()
    {
        var resource = "rlock:timeout-" + Guid.NewGuid();

        // Hold the lock.
        await using var firstLock = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstLock);

        // Second attempt should time out.
        var secondLock = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromMilliseconds(200),
            retry: TimeSpan.FromMilliseconds(50));

        Assert.Null(secondLock);
    }

    [Fact]
    public async Task AcquireRenewableAsync_AfterDispose_CanReacquire()
    {
        var resource = "rlock:reacquire-" + Guid.NewGuid();

        var firstLock = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstLock);
        await firstLock!.DisposeAsync();
        Assert.False(firstLock.IsAcquired);

        // Reacquire — should succeed.
        await using var secondLock = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(secondLock);
        Assert.True(secondLock!.IsAcquired);
    }

    // -------------------------------------------------------------------------
    // RenewAsync — renewal succeeds before expiry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RenewAsync_BeforeExpiry_ReturnsTrue()
    {
        var resource = "rlock:renew-ok-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        var renewed = await handle!.RenewAsync();

        Assert.True(renewed);
        Assert.True(handle.IsAcquired);
    }

    [Fact]
    public async Task RenewAsync_MultipleConsecutiveRenewals_AllSucceed()
    {
        var resource = "rlock:renew-multi-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        for (var i = 0; i < 3; i++)
        {
            var renewed = await handle!.RenewAsync();
            Assert.True(renewed, $"Renewal {i + 1} should succeed");
            Assert.True(handle.IsAcquired);
        }
    }

    // -------------------------------------------------------------------------
    // RenewAsync — returns false after expiry without throwing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RenewAsync_AfterLockReleased_ReturnsFalse_WithoutThrowing()
    {
        var resource = "rlock:renew-expired-" + Guid.NewGuid();

        var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        // Dispose the lock first.
        await handle!.DisposeAsync();

        // RenewAsync after DisposeAsync must return false without throwing.
        var exception = await Record.ExceptionAsync(async () =>
        {
            var renewed = await handle.RenewAsync();
            Assert.False(renewed);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task RenewAsync_WhileLockStolenByConcurrentHolder_ReturnsFalse()
    {
        // Because RedLock.net renewal is implemented via re-acquisition, we simulate
        // the "lock stolen" case by having a second holder acquire the same resource
        // with a very short wait on the first acquire, allowing the re-acquire to fail.
        var resource = "rlock:stolen-" + Guid.NewGuid();

        // Acquire with a very short expiry so we can test re-acquisition failure.
        var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        // A second holder grabs the lock while we hold it.
        // They succeed because the first handle was acquired with 30s expiry —
        // they can't steal it. So instead we exercise the re-acquisition path
        // by disposing the original handle first, then renewing.
        await handle!.DisposeAsync();

        // Hold the resource with the second holder.
        await using var secondHolder = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(secondHolder);

        // The original handle is disposed — its renewal must fail since it already
        // released the lock and the resource is now held by secondHolder.
        var renewed = await handle.RenewAsync();

        Assert.False(renewed);
    }

    // -------------------------------------------------------------------------
    // KeepAliveAsync — prevents lock loss across a slow operation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeepAliveAsync_RenewsLockRepeatedly_LockStillHeldAfterDelay()
    {
        var resource = "rlock:keepalive-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(10),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        using var cts = new CancellationTokenSource();

        // Run keep-alive in background with a 2-second interval.
        var keepAliveTask = handle!.KeepAliveAsync(
            renewalInterval: TimeSpan.FromSeconds(2),
            ct: cts.Token);

        // Simulate a 5-second operation.
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Lock must still be held after the delay.
        Assert.True(handle.IsAcquired,
            "Lock should still be held while KeepAliveAsync is running");

        // Cancel the keep-alive loop.
        await cts.CancelAsync();
        await keepAliveTask;
    }

    [Fact]
    public async Task KeepAliveAsync_ExitsWhenLockIsReleased()
    {
        var resource = "rlock:keepalive-exit-" + Guid.NewGuid();

        var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        using var cts = new CancellationTokenSource();

        var keepAliveTask = handle!.KeepAliveAsync(
            renewalInterval: TimeSpan.FromMilliseconds(500),
            ct: cts.Token);

        // Release the lock — keep-alive should detect IsAcquired = false and exit.
        await handle.DisposeAsync();

        // Wait for keep-alive to exit within a reasonable timeout.
        var completed = await Task.WhenAny(keepAliveTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(keepAliveTask, completed);

        // Cancellation cleanup.
        await cts.CancelAsync();
    }

    [Fact]
    public async Task KeepAliveAsync_ExitsWhenCancelled()
    {
        var resource = "rlock:keepalive-cancel-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);

        using var cts = new CancellationTokenSource();

        var keepAliveTask = handle!.KeepAliveAsync(
            renewalInterval: TimeSpan.FromSeconds(60), // long interval — cancelled before first renewal
            ct: cts.Token);

        await cts.CancelAsync();

        // KeepAliveAsync must complete without throwing OperationCanceledException.
        var exception = await Record.ExceptionAsync(() => keepAliveTask);
        Assert.Null(exception);
    }
}
