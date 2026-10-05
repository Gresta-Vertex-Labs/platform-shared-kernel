using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// An unreachable lock store must surface as <see cref="DistributedLockUnavailableException"/>,
/// never as the <see langword="null"/> that means contention, and a held lock must be reported lost
/// before its key can expire on the server.
/// </summary>
public sealed class LockStoreUnavailableTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public LockStoreUnavailableTests()
    {
        _provider = BuildProvider($"127.0.0.1:{GetClosedPort()},connectRetry=0");
    }

    private IDistributedLockService LockService => _provider.GetRequiredService<IDistributedLockService>();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    [Fact]
    public async Task TryAcquireAsync_StoreUnreachable_ThrowsDistributedLockUnavailableException()
    {
        var resource = RedisFixture.NewResource("unreachable-lock");

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => LockService.TryAcquireAsync(resource).AsTask());

        Assert.Equal(resource, ex.Resource);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TryAcquireAsync_WithWaitTime_StoreUnreachable_ThrowsInsteadOfWaiting()
    {
        var resource = RedisFixture.NewResource("unreachable-wait");
        var stopwatch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => LockService.TryAcquireAsync(resource, new DistributedLockOptions { WaitTime = TimeSpan.FromSeconds(30) }).AsTask());

        Assert.Equal(resource, ex.Resource);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"The attempt waited {stopwatch.Elapsed} instead of failing.");
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_StoreUnreachable_ThrowsDistributedLockUnavailableException()
    {
        var resource = RedisFixture.NewResource("unreachable-lease");

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => LockService.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(10)).AsTask());

        Assert.Equal(resource, ex.Resource);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task HeldLock_StoreStops_IsReportedLost_AndNewAttemptsThrow()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();

        await using var provider = BuildProvider(container.GetConnectionString());
        var lockService = provider.GetRequiredService<IDistributedLockService>();

        var expiry = TimeSpan.FromSeconds(3);
        await using var handle = await lockService.TryAcquireAsync(
            RedisFixture.NewResource("store-stops"),
            new DistributedLockOptions { Expiry = expiry });
        Assert.NotNull(handle);

        await container.StopAsync();

        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (handle.LostToken.Register(() => lost.TrySetResult()))
        {
            await lost.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        Assert.False(handle.IsHeld);

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => lockService.TryAcquireAsync(RedisFixture.NewResource("store-stopped")).AsTask());
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// Freezes the Redis process (docker pause) so extensions neither succeed nor get a reply. The lock must be
    /// reported lost at five sixths of its expiry, before the key can expire, and must stop extending the key.
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task HeldLock_StoreFreezes_IsReportedLostBeforeExpiry_AndNoLongerExtendsTheKey()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();

        await using var provider = BuildProvider(container.GetConnectionString());
        var lockService = provider.GetRequiredService<IDistributedLockService>();
        await using var admin = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        var adminDb = admin.GetDatabase();

        // Keep-alive ticks every 3 s; loss is due 7.5 s after the acquire request was sent.
        var expiry = TimeSpan.FromSeconds(9);
        var resource = RedisFixture.NewResource("store-freezes");
        var lockKey = RedisFixture.LockKey(resource);

        var sinceBeforeAcquire = Stopwatch.StartNew();
        await using var handle = await lockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = expiry });
        Assert.NotNull(handle);
        var owner = await adminDb.StringGetAsync(lockKey);
        Assert.False(owner.IsNull);

        var lostAt = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = handle.LostToken.Register(() => lostAt.TrySetResult(sinceBeforeAcquire.Elapsed));

        await container.PauseAsync();
        var paused = true;
        try
        {
            // Otherwise the first extension may succeed and move both the key's expiry and the loss deadline.
            Assert.True(sinceBeforeAcquire.Elapsed < expiry / 3,
                $"Pausing Redis took until {sinceBeforeAcquire.Elapsed}, past the first keep-alive tick; the test cannot tell expiry from loss.");

            var elapsedAtLoss = await lostAt.Task.WaitAsync(expiry + TimeSpan.FromSeconds(10));

            Assert.False(handle.IsHeld);
            Assert.True(elapsedAtLoss < expiry - TimeSpan.FromMilliseconds(500),
                $"The lock was reported lost {elapsedAtLoss} after acquisition started; the key could expire at {expiry}.");
            Assert.True(elapsedAtLoss >= expiry * 5 / 6 - TimeSpan.FromMilliseconds(250),
                $"The lock was reported lost too early ({elapsedAtLoss}); loss is due at five sixths of {expiry}.");

            // Unpause only once the key has expired on the server, so an extension still queued in the socket finds no key.
            var untilExpired = expiry + TimeSpan.FromSeconds(1) - sinceBeforeAcquire.Elapsed;
            if (untilExpired > TimeSpan.Zero)
                await Task.Delay(untilExpired);

            await container.UnpauseAsync();
            paused = false;
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.False(await adminDb.KeyExistsAsync(lockKey));

            // Put the key back as if still owned: a running keep-alive would extend it past its short expiry.
            Assert.True(await adminDb.StringSetAsync(lockKey, owner, TimeSpan.FromSeconds(4)));
            await Task.Delay(TimeSpan.FromSeconds(5.5));

            Assert.False(await adminDb.KeyExistsAsync(lockKey), "The lost lock's keep-alive extended the key.");

            await using var next = await lockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = expiry });
            Assert.NotNull(next);
            Assert.True(next.FencingToken > handle.FencingToken);
        }
        finally
        {
            if (paused)
                await container.UnpauseAsync();
        }
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddRedisConnection(o =>
            {
                o.ConnectionString = connectionString;
                // Short timeouts keep failing commands from waiting on the default five seconds.
                o.ConnectTimeout = TimeSpan.FromMilliseconds(500);
                o.CommandTimeout = TimeSpan.FromMilliseconds(500);
            })
            .AddRedisDistributedLocking();
        return services.BuildServiceProvider();
    }

    private static int GetClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
