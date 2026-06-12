using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Integration tests for <see cref="IDistributedLockService"/> backed by RedLock.net.
/// Uses Testcontainers to spin up a real Redis instance.
/// </summary>
[Collection("Redis")]
public sealed class RedLockIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task AcquireAsync_HappyPath_ReturnsNonNullHandle()
    {
        var resource = "lock:test-acquire-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task AcquireAsync_LockHeld_WaitExpires_ReturnsNull()
    {
        var resource = "lock:test-timeout-" + Guid.NewGuid();

        // Acquire the lock first and hold it.
        await using var firstHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstHandle);

        // Second attempt should time out quickly and return null.
        var secondHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromMilliseconds(200), // short wait
            retry: TimeSpan.FromMilliseconds(50));

        Assert.Null(secondHandle);
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_CanReacquire()
    {
        var resource = "lock:test-reacquire-" + Guid.NewGuid();

        // Acquire and immediately release.
        var firstHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstHandle);
        await firstHandle!.DisposeAsync();

        // Now reacquire — should succeed.
        await using var secondHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(secondHandle);
    }

    [Fact]
    public async Task AcquireAsync_NullOrWhitespaceResource_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            LockService.AcquireAsync(
                "",
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            LockService.AcquireAsync(
                "   ",
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task AcquireAsync_ZeroExpiry_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            LockService.AcquireAsync(
                "resource:zero-expiry",
                expiry: TimeSpan.Zero,
                wait: TimeSpan.FromSeconds(1),
                retry: TimeSpan.FromMilliseconds(100)));
    }
}
