using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Diagnostics;
using SharedKernel.Caching.FusionCache.Encryption;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Health;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

/// <summary>
/// The FusionCache package's log statements: ids stay in the package sub-block and keep their numbers, every
/// template follows the conventions, no id reaches a log, and every caught-exception path passes the exception.
/// </summary>
public sealed class FusionCacheLoggingTests
{
    private const int SubBlock = 0;

    [Fact]
    public void EventIds_KeepTheirNumbers_InsideTheFusionCacheSubBlock()
    {
        var ids = new Dictionary<string, int>
        {
            [nameof(FusionCacheLoggingEventIds.WaitingForWarmup)] = 2000,
            [nameof(FusionCacheLoggingEventIds.NoWarmupStrategies)] = 2001,
            [nameof(FusionCacheLoggingEventIds.WarmupStarting)] = 2002,
            [nameof(FusionCacheLoggingEventIds.WarmupCancelled)] = 2003,
            [nameof(FusionCacheLoggingEventIds.StrategyExecuting)] = 2004,
            [nameof(FusionCacheLoggingEventIds.StrategyCompleted)] = 2005,
            [nameof(FusionCacheLoggingEventIds.StrategyFailed)] = 2006,
            [nameof(FusionCacheLoggingEventIds.WarmupCompleted)] = 2007,
            [nameof(FusionCacheLoggingEventIds.CacheMiss)] = 2010,
            [nameof(FusionCacheLoggingEventIds.CacheSet)] = 2011,
            [nameof(FusionCacheLoggingEventIds.FactoryInvoked)] = 2012,
            [nameof(FusionCacheLoggingEventIds.CacheRemoved)] = 2013,
            [nameof(FusionCacheLoggingEventIds.CacheTagRemoved)] = 2014,
            [nameof(FusionCacheLoggingEventIds.DecryptFailed)] = 2015,
            [nameof(FusionCacheLoggingEventIds.CacheExpired)] = 2016,
            [nameof(FusionCacheLoggingEventIds.CacheCleared)] = 2017,
            [nameof(FusionCacheLoggingEventIds.EvictAfterDecryptFailureFailed)] = 2018,
            [nameof(FusionCacheLoggingEventIds.ProbeFailed)] = 2019,
        };

        foreach (var (name, expected) in ids)
        {
            var actual = (int)typeof(FusionCacheLoggingEventIds).GetField(name)!.GetRawConstantValue()!;
            Assert.Equal(expected, actual);
            Assert.True(LoggingEventIdRanges.IsInSubBlock(actual, LoggingEventIdRanges.Caching, SubBlock), name);
        }

        Assert.Equal(
            typeof(FusionCacheLoggingEventIds).GetFields().Count(f => f.IsLiteral && f.Name != nameof(FusionCacheLoggingEventIds.Base)),
            ids.Count);
    }

    [Fact]
    public async Task CacheOperations_LogWithConventionalTemplates_AndNeverAnId()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        var key = CacheKeyFormat.BuildKey("svc", "orders", "secret-id-42");

        await cache.TryGetAsync<string>(key);
        await cache.SetAsync(key, "v", CachePolicy.Default.WithTags("open"));
        await cache.GetOrSetAsync(key + "-b", _ => ValueTask.FromResult("f"), CachePolicy.Default);
        await cache.ExpireAsync(key);
        await cache.RemoveAsync(key);
        await cache.RemoveByTagAsync("open");
        await cache.ClearAsync();

        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        factory.ShouldHaveCategory(typeof(FusionCacheService));
        var records = factory.GetLogger(typeof(FusionCacheService).FullName!).Records;

        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheMiss);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheSet);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.FactoryInvoked);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheExpired);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheRemoved);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheTagRemoved);
        records.ShouldHaveLogged(FusionCacheLoggingEventIds.CacheCleared, LogLevel.Warning);
        records.ShouldHaveLoggedTemplate(FusionCacheLoggingEventIds.CacheMiss, "Cache miss for '{KeyPrefix}'.");
        records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Caching, SubBlock)
            .ShouldNotLeak("secret-id-42");
    }

    [Fact]
    public async Task WarmupStrategyFailure_LogsTheException_AndWarmupContinues()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "svc";
            o.WaitForWarmup = true;
        });
        services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy());
        await using var provider = services.BuildServiceProvider();
        var logger = new InMemoryLogger<CacheWarmupHostedService>();
        using var service = new CacheWarmupHostedService(provider, provider.GetRequiredService<IOptions<CachingOptions>>(), logger);

        await service.StartingAsync(CancellationToken.None);

        var failed = logger.Records.ShouldHaveLoggedWithException(FusionCacheLoggingEventIds.StrategyFailed, typeof(InvalidOperationException));
        Assert.Equal(LogLevel.Error, failed.LogLevel);
        logger.Records.ShouldHaveLogged(FusionCacheLoggingEventIds.WaitingForWarmup);
        logger.Records.ShouldHaveLogged(FusionCacheLoggingEventIds.WarmupCompleted);
        logger.Records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Caching, SubBlock);
    }

    [Fact]
    public async Task DecryptFailure_LogsTheErrorCode_AndAFailedEvictionPassesItsException()
    {
        var inner = Substitute.For<ICacheService>();
        inner.TryGetAsync<byte[]>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CacheLookup<byte[]>>(CacheLookup<byte[]>.Hit([1, 2, 3])));
        inner.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException(new InvalidOperationException("eviction failed")));
        var logger = new InMemoryLogger<EncryptedCacheService>();
        var sut = new EncryptedCacheService(
            inner,
            Substitute.For<ISymmetricEncryptionService>(),
            new JsonSerializerOptions(),
            compression: null,
            logger);

        var lookup = await sut.TryGetAsync<string>("svc:orders:secret-id-42");

        Assert.False(lookup.IsHit);
        logger.Records.ShouldHaveLoggedWithoutException(FusionCacheLoggingEventIds.DecryptFailed);
        var evictFailed = logger.Records.ShouldHaveLoggedWithException(
            FusionCacheLoggingEventIds.EvictAfterDecryptFailureFailed, typeof(InvalidOperationException));
        Assert.Equal(LogLevel.Warning, evictFailed.LogLevel);
        logger.Records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Caching, SubBlock);
        Assert.All(logger.Records, record => Assert.DoesNotContain("secret-id-42", record.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeFailure_LogsAtDebugWithTheException_AndReportsDegraded()
    {
        var cache = Substitute.For<ICacheService>();
        cache.TryGetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ValueTask.FromException<CacheLookup<string>>(new InvalidOperationException("cache down")));
        await using var provider = new ServiceCollection().AddSingleton(cache).BuildServiceProvider();
        var logger = new InMemoryLogger<CacheReadinessProbe>();
        var probe = new CacheReadinessProbe(provider, logger);

        var report = await probe.ProbeAsync();

        Assert.Equal(ReadinessStatus.Degraded, report.Status);
        var record = logger.Records.ShouldHaveLoggedWithException(FusionCacheLoggingEventIds.ProbeFailed, typeof(InvalidOperationException));
        Assert.Equal(LogLevel.Debug, record.LogLevel);
        logger.Records.ShouldFollowLogConvention();
    }

    private sealed class ThrowingStrategy : ICacheWarmupStrategy
    {
        public string Name => "failing";

        public int Order => 1;

        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) =>
            throw new InvalidOperationException("strategy failed");
    }
}
