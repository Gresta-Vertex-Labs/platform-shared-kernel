using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// An unreachable lock store must surface as <see cref="DistributedLockUnavailableException"/>,
/// never as the <see langword="null"/> that means contention.
/// </summary>
public sealed class LockStoreUnavailableTests : IAsyncDisposable
{
    // Short timeouts keep the failing commands from waiting on the default five seconds.
    private const string FastFailOptions = ",connectRetry=0,asyncTimeout=500,syncTimeout=500";

    private readonly ServiceProvider _provider;

    public LockStoreUnavailableTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking(
            $"127.0.0.1:{GetClosedPort()}{FastFailOptions}",
            o => o.ConnectTimeoutMs = 500);
        _provider = services.BuildServiceProvider();
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

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => LockService.TryAcquireAsync(resource, new DistributedLockOptions { WaitTime = TimeSpan.FromSeconds(30) }).AsTask());

        Assert.Equal(resource, ex.Resource);
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
    public async Task HeldLock_StoreStopsPastExpiry_IsReportedLost_AndNewAttemptsThrow()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking(container.GetConnectionString() + FastFailOptions, o => o.ConnectTimeoutMs = 500);
        await using var provider = services.BuildServiceProvider();
        var lockService = provider.GetRequiredService<IDistributedLockService>();

        var resource = RedisFixture.NewResource("store-stops");
        await using var handle = await lockService.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(1) });
        Assert.NotNull(handle);

        await container.StopAsync();

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), handle.LostToken);
            Assert.Fail("LostToken was not cancelled after the store became unreachable.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.False(handle.IsHeld);

        var ex = await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => lockService.TryAcquireAsync(RedisFixture.NewResource("store-stopped")).AsTask());
        Assert.NotNull(ex.InnerException);
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
