using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Redis-specific behaviour of locks and leases: keep-alive, loss detection, owner-checked release,
/// key expiry and the fencing counter, verified by inspecting the keys directly.
/// </summary>
[Collection("Redis")]
public sealed class RedisDistributedLockTests(RedisFixture fixture)
{
    private IDistributedLockService LockService => fixture.LockService;

    private IDatabase Database => fixture.Database;

    // -------------------------------------------------------------------------
    // Keep-alive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HeldLock_OutlivesItsExpiry_WhileKeptAlive()
    {
        var resource = RedisFixture.NewResource("keep-alive");
        var expiry = TimeSpan.FromSeconds(1);

        await using var handle = await LockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = expiry });
        Assert.NotNull(handle);

        await Task.Delay(TimeSpan.FromSeconds(3));

        var ttl = await Database.KeyTimeToLiveAsync(RedisFixture.LockKey(resource));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMilliseconds(1), expiry);
        Assert.True(handle.IsHeld);
        Assert.False(handle.LostToken.IsCancellationRequested);
        Assert.Null(await LockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = expiry }));
    }

    [Fact]
    public async Task TryAcquireAsync_SetsLockKeyWithExpiry()
    {
        var resource = RedisFixture.NewResource("lock-ttl");
        var expiry = TimeSpan.FromSeconds(10);

        await using var handle = await LockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = expiry });
        Assert.NotNull(handle);

        var ttl = await Database.KeyTimeToLiveAsync(RedisFixture.LockKey(resource));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMilliseconds(1), expiry);
    }

    // -------------------------------------------------------------------------
    // Loss detection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HeldLock_KeyDeleted_IsReportedLost()
    {
        var resource = RedisFixture.NewResource("lost-deleted");

        await using var handle = await LockService.TryAcquireAsync(
            resource,
            new DistributedLockOptions { Expiry = TimeSpan.FromMilliseconds(900) });
        Assert.NotNull(handle);

        await Database.KeyDeleteAsync(RedisFixture.LockKey(resource));

        Assert.True(await WaitForLossAsync(handle, TimeSpan.FromSeconds(5)), "LostToken was not cancelled.");
        Assert.False(handle.IsHeld);
    }

    [Fact]
    public async Task HeldLock_KeyTakenByAnotherOwner_IsReportedLost_AndReleaseKeepsTheOtherOwnersKey()
    {
        var resource = RedisFixture.NewResource("lost-overwritten");
        var lockKey = RedisFixture.LockKey(resource);

        var handle = await LockService.TryAcquireAsync(
            resource,
            new DistributedLockOptions { Expiry = TimeSpan.FromMilliseconds(900) });
        Assert.NotNull(handle);

        await Database.StringSetAsync(lockKey, "another-owner", TimeSpan.FromSeconds(30));

        Assert.True(await WaitForLossAsync(handle, TimeSpan.FromSeconds(5)), "LostToken was not cancelled.");
        Assert.False(handle.IsHeld);

        await handle.DisposeAsync();

        Assert.Equal("another-owner", (string?)await Database.StringGetAsync(lockKey));
    }

    // -------------------------------------------------------------------------
    // Release
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_DeletesOwnLockKey_AndKeepsFencingCounter()
    {
        var resource = RedisFixture.NewResource("release");

        var handle = await LockService.TryAcquireAsync(resource);
        Assert.NotNull(handle);

        await handle.DisposeAsync();

        Assert.False(await Database.KeyExistsAsync(RedisFixture.LockKey(resource)));
        Assert.Equal(handle.FencingToken, (long)await Database.StringGetAsync(RedisFixture.FencingKey(resource)));
        Assert.Null(await Database.KeyTimeToLiveAsync(RedisFixture.FencingKey(resource)));
    }

    [Fact]
    public async Task DisposeAsync_NeverDeletesAnotherOwnersLock()
    {
        var resource = RedisFixture.NewResource("release-other-owner");
        var lockKey = RedisFixture.LockKey(resource);

        // A long expiry keeps the keep-alive loop from noticing the takeover before release.
        var handle = await LockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30) });
        Assert.NotNull(handle);

        await Database.StringSetAsync(lockKey, "another-owner");

        await handle.DisposeAsync();

        Assert.Equal("another-owner", (string?)await Database.StringGetAsync(lockKey));
        Assert.Null(await LockService.TryAcquireAsync(resource));
    }

    // -------------------------------------------------------------------------
    // Leases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireLeaseAsync_SetsLockKeyWithDurationExpiry()
    {
        var resource = RedisFixture.NewResource("lease-ttl");
        var duration = TimeSpan.FromSeconds(10);

        var lease = await LockService.TryAcquireLeaseAsync(resource, duration);
        Assert.NotNull(lease);

        var ttl = await Database.KeyTimeToLiveAsync(RedisFixture.LockKey(resource));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMilliseconds(1), duration);
        Assert.Null(await Database.KeyTimeToLiveAsync(RedisFixture.FencingKey(resource)));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_KeyExpiresAfterDuration()
    {
        var resource = RedisFixture.NewResource("lease-key-expires");
        var duration = TimeSpan.FromMilliseconds(300);

        var lease = await LockService.TryAcquireLeaseAsync(resource, duration);
        Assert.NotNull(lease);

        await Task.Delay(duration + TimeSpan.FromMilliseconds(300));

        Assert.False(await Database.KeyExistsAsync(RedisFixture.LockKey(resource)));
    }

    // -------------------------------------------------------------------------
    // Fencing counter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContendedAttempts_DoNotAdvanceTheFencingCounter()
    {
        var resource = RedisFixture.NewResource("contended-counter");

        var holder = await LockService.TryAcquireAsync(resource);
        Assert.NotNull(holder);

        for (var i = 0; i < 3; i++)
        {
            Assert.Null(await LockService.TryAcquireAsync(resource));
            Assert.Null(await LockService.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(5)));
        }

        await holder.DisposeAsync();

        await using var next = await LockService.TryAcquireAsync(resource);
        Assert.NotNull(next);
        Assert.Equal(holder.FencingToken + 1, next.FencingToken);
    }

    [Fact]
    public async Task FencingCounters_AreIndependentPerResource()
    {
        var resourceA = RedisFixture.NewResource("independent-a");

        for (var i = 0; i < 3; i++)
        {
            var handle = await LockService.TryAcquireAsync(resourceA);
            Assert.NotNull(handle);
            await handle.DisposeAsync();
        }

        await using var firstOnB = await LockService.TryAcquireAsync(RedisFixture.NewResource("independent-b"));
        var firstLeaseOnC = await LockService.TryAcquireLeaseAsync(RedisFixture.NewResource("independent-c"), TimeSpan.FromSeconds(5));

        Assert.NotNull(firstOnB);
        Assert.NotNull(firstLeaseOnC);
        Assert.Equal(1L, firstOnB.FencingToken);
        Assert.Equal(1L, firstLeaseOnC.FencingToken);
    }

    private static async Task<bool> WaitForLossAsync(IDistributedLock handle, TimeSpan timeout)
    {
        try
        {
            await Task.Delay(timeout, handle.LostToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
