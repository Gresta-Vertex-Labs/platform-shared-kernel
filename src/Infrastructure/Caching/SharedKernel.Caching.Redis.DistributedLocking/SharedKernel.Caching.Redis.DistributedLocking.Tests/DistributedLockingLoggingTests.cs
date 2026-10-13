using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Diagnostics;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// The lock service's log statements against a real Redis: ids stay in the package sub-block and keep their numbers,
/// templates follow the conventions, a resource is the only caller-chosen text, and every caught store failure passes
/// its exception.
/// </summary>
[Collection("Redis")]
public sealed class DistributedLockingLoggingTests(RedisFixture fixture)
{
    private const int SubBlock = 3;

    private static readonly string LoggerCategory = typeof(RedisDistributedLockService).FullName!;

    [Fact]
    public void EventIds_KeepTheirNumbers_InsideTheLockingSubBlock()
    {
        Assert.Equal(2300, DistributedLockingLoggingEventIds.LockAcquired);
        Assert.Equal(2301, DistributedLockingLoggingEventIds.LockContended);
        Assert.Equal(2302, DistributedLockingLoggingEventIds.LeaseAcquired);
        Assert.Equal(2303, DistributedLockingLoggingEventIds.LeaseContended);
        Assert.Equal(2304, DistributedLockingLoggingEventIds.LockReleased);
        Assert.Equal(2305, DistributedLockingLoggingEventIds.ReleaseFailed);
        Assert.Equal(2306, DistributedLockingLoggingEventIds.ExtendFailed);
        Assert.Equal(2307, DistributedLockingLoggingEventIds.LockLost);
        Assert.All(
            typeof(DistributedLockingLoggingEventIds).GetFields()
                .Where(f => f.IsLiteral && f.Name != nameof(DistributedLockingLoggingEventIds.Base)),
            field => Assert.True(
                LoggingEventIdRanges.IsInSubBlock((int)field.GetRawConstantValue()!, LoggingEventIdRanges.Caching, SubBlock),
                field.Name));
    }

    [Fact]
    public async Task AcquireContendReleaseAndLease_LogConventionalDebugStatements_WithTheResourceQuoted()
    {
        await using var provider = BuildProvider(fixture.ConnectionString, out var loggers);
        var locks = provider.GetRequiredService<IDistributedLockService>();
        var resource = RedisFixture.NewResource("logging");

        var handle = await locks.TryAcquireAsync(resource);
        Assert.NotNull(handle);
        Assert.Null(await locks.TryAcquireAsync(resource));
        await handle.DisposeAsync();
        var lease = await locks.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(5));
        Assert.NotNull(lease);
        Assert.Null(await locks.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(5)));

        var records = loggers.GetLogger(LoggerCategory).Records;
        var acquired = records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LockAcquired, LogLevel.Debug);
        records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LockContended, LogLevel.Debug);
        records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LockReleased, LogLevel.Debug);
        records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LeaseAcquired, LogLevel.Debug);
        records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LeaseContended, LogLevel.Debug);
        records.ShouldHaveLoggedTemplate(
            DistributedLockingLoggingEventIds.LockAcquired,
            "Distributed lock acquired on '{Resource}' with fencing token {FencingToken}.");
        Assert.Contains($"'{resource}'", acquired.Message, StringComparison.Ordinal);
        records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Caching, SubBlock);
        loggers.ShouldHaveCategory(typeof(RedisDistributedLockService));
    }

    [Fact]
    public async Task LostLock_LogsTheLossCauseAtError_WhenAnotherOwnerTakesTheKey()
    {
        await using var provider = BuildProvider(fixture.ConnectionString, out var loggers);
        var locks = provider.GetRequiredService<IDistributedLockService>();
        var resource = RedisFixture.NewResource("logging-lost");

        await using var handle = await locks.TryAcquireAsync(resource, new DistributedLockOptions { Expiry = TimeSpan.FromMilliseconds(900) });
        Assert.NotNull(handle);
        await fixture.Database.StringSetAsync(RedisFixture.LockKey(resource), "another-owner", TimeSpan.FromSeconds(30));

        var lost = await WaitForAsync(() => loggers.GetLogger(LoggerCategory).Records.Any(r => r.EventId.Id == DistributedLockingLoggingEventIds.LockLost));
        Assert.True(lost, "No lock-lost log was written.");

        var records = loggers.GetLogger(LoggerCategory).Records;
        var record = records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LockLost, LogLevel.Error);
        Assert.True(record.TryGetProperty("LossCause", out var cause));
        Assert.Equal(LockLossCause.OwnerChanged, cause);
        records.ShouldHaveLoggedTemplate(
            DistributedLockingLoggingEventIds.LockLost,
            "Distributed lock on '{Resource}' with fencing token {FencingToken} was lost because of '{LossCause}'.");
    }

    [Fact(Timeout = 90_000)]
    public async Task StoreStops_ExtendFailureAndLossAreLogged_WithTheExtendExceptionPassed()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        await using var provider = BuildProvider(container.GetConnectionString(), out var loggers);
        var locks = provider.GetRequiredService<IDistributedLockService>();

        await using var handle = await locks.TryAcquireAsync(
            RedisFixture.NewResource("logging-store-stops"),
            new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(3) });
        Assert.NotNull(handle);

        await container.StopAsync();

        var logged = await WaitForAsync(
            () => handle.LostToken.IsCancellationRequested
                && loggers.GetLogger(LoggerCategory).Records.Any(r => r.EventId.Id == DistributedLockingLoggingEventIds.ExtendFailed),
            TimeSpan.FromSeconds(30));
        Assert.True(logged, "The extend failure and the loss were not both logged.");

        var records = loggers.GetLogger(LoggerCategory).Records;
        var extendFailed = records.ShouldHaveLogged(DistributedLockingLoggingEventIds.ExtendFailed, LogLevel.Warning);
        Assert.NotNull(extendFailed.Exception);
        var lossRecord = records.ShouldHaveLogged(DistributedLockingLoggingEventIds.LockLost, LogLevel.Error);
        Assert.True(lossRecord.TryGetProperty("LossCause", out var cause));
        Assert.Equal(LockLossCause.DeadlineElapsed, cause);
    }

    [Fact(Timeout = 90_000)]
    public async Task ReleaseFailure_IsLogged_WithTheStoreException()
    {
        await using var container = new RedisBuilder("redis:7-alpine").Build();
        await container.StartAsync();
        await using var provider = BuildProvider(container.GetConnectionString(), out var loggers);
        var locks = provider.GetRequiredService<IDistributedLockService>();

        var handle = await locks.TryAcquireAsync(
            RedisFixture.NewResource("logging-release-fails"),
            new DistributedLockOptions { Expiry = TimeSpan.FromSeconds(30) });
        Assert.NotNull(handle);

        await container.StopAsync();
        await handle.DisposeAsync();

        var records = loggers.GetLogger(LoggerCategory).Records;
        var failed = records.ShouldHaveLogged(DistributedLockingLoggingEventIds.ReleaseFailed, LogLevel.Warning);
        Assert.NotNull(failed.Exception);
        records.ShouldHaveLoggedTemplate(
            DistributedLockingLoggingEventIds.ReleaseFailed,
            "Distributed lock on '{Resource}' could not be released, and it expires on its own.");
    }

    private static ServiceProvider BuildProvider(string connectionString, out InMemoryLoggerFactory loggers)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services
            .AddRedisConnection(o =>
            {
                o.ConnectionString = connectionString;
                o.ConnectTimeout = TimeSpan.FromMilliseconds(500);
                o.CommandTimeout = TimeSpan.FromMilliseconds(500);
            })
            .AddRedisDistributedLocking();
        var provider = services.BuildServiceProvider();
        loggers = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        return provider;
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(50);
        }

        return condition();
    }
}
