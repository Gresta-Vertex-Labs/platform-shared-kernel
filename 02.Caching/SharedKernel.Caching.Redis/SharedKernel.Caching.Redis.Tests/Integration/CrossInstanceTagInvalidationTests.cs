using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.Integration;

/// <summary>
/// Proves that <see cref="ICacheService.RemoveByTagAsync"/> propagates across two
/// <strong>independently-constructed</strong> <see cref="ICacheService"/>/FusionCache
/// instances that share one Redis L2 backplane — the exact topology of two pods of the same
/// microservice in production (Phase 39 / P-302 / WO-050).
/// </summary>
/// <remarks>
/// <para>
/// Every other Redis-backed test in this suite (e.g. <see cref="RedisL2IntegrationTests"/>)
/// constructs a single shared <see cref="ServiceProvider"/>, which cannot distinguish "works
/// across pods" from "works within one process." This suite builds <strong>two</strong> fully
/// independent DI containers — each with its own <c>IConnectionMultiplexer</c> (via its own
/// <c>AddRedisConnection</c> call inside <c>AddRedisL2</c>), its own L1 <c>MemoryCache</c>, and
/// its own <c>IFusionCache</c> instance — both pointed at the same Testcontainers Redis
/// connection string and the same <c>CachingCoreOptions.ServiceName</c>.
/// </para>
/// <para>
/// The propagation proof is black-box: it never inspects FusionCache's internal tagging
/// mechanism (an implementation detail this domain does not own). Instead it seeds a tagged
/// entry via instance A, reads it through on instance B (populating B's own L1), invalidates
/// the tag via instance A only, then polls instance B with a new, distinguishable factory
/// return value via <c>GetOrSetAsync</c> until the fresh value is observed or a bounded
/// timeout elapses.
/// </para>
/// </remarks>
[Collection("Redis")]
public sealed class CrossInstanceTagInvalidationTests : IAsyncLifetime
{
    // 5 seconds mirrors this domain's existing timing-sensitive test tolerance precedent
    // (Phase 23's renewable-lock renewal-timing tests) — long enough to absorb CI runner
    // scheduling jitter and Redis Pub/Sub relay latency across two independent connections,
    // short enough that a genuine propagation gap fails the test in a reasonable time rather
    // than hanging the suite.
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _providerA;
    private ServiceProvider? _providerB;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var connectionString = _redisContainer.GetConnectionString();

        // Two fully independent DI containers — never two scopes of one container. Each
        // produces its own IConnectionMultiplexer, its own L1 MemoryCache, and its own
        // IFusionCache instance, representing two pods of one microservice sharing the same
        // Redis L2 backplane and the same ServiceName.
        _providerA = BuildInstance(connectionString);
        _providerB = BuildInstance(connectionString);

        // Eagerly resolve ICacheService on both instances so each FusionCache's Redis
        // backplane subscription is established before the test body runs — mirrors this
        // suite's established "allow subscription to establish" timing precedent for Pub/Sub.
        _ = CacheOf(_providerA);
        _ = CacheOf(_providerB);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
    }

    public async Task DisposeAsync()
    {
        if (_providerA is not null)
            await _providerA.DisposeAsync();

        if (_providerB is not null)
            await _providerB.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private static ServiceProvider BuildInstance(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddSharedKernelCaching(o => o.ServiceName = "cross-instance-svc")
            .AddRedisL2(connectionString);

        return services.BuildServiceProvider();
    }

    private static ICacheService CacheOf(ServiceProvider provider) =>
        provider.GetRequiredService<ICacheService>();

    [Fact]
    public async Task RemoveByTagAsync_OnInstanceA_EvictsInstanceBsStaleL1EntryWithinBoundedWait()
    {
        var tag = "cross-instance-tag-" + Guid.NewGuid();
        var key = "cross-instance:tagged:" + Guid.NewGuid();
        var policy = CachePolicy.Default.WithTags(tag);

        var cacheA = CacheOf(_providerA!);
        var cacheB = CacheOf(_providerB!);

        // Seed the tagged entry via instance A only.
        await cacheA.SetAsync(key, "original-value", policy);

        // Read-through on instance B: L1 miss on B, L2 hit via the shared Redis backplane —
        // populates B's own L1 with the value A wrote. The factory must never be invoked here
        // since Redis L2 already holds the value.
        var factoryInvokedBeforeInvalidation = false;
        var initialValue = await cacheB.GetOrSetAsync(
            key,
            async _ =>
            {
                factoryInvokedBeforeInvalidation = true;
                return "should-not-be-returned";
            },
            policy);

        Assert.Equal("original-value", initialValue);
        Assert.False(factoryInvokedBeforeInvalidation);

        // Tag-invalidate via instance A only. Instance B is never touched directly — the only
        // channel available to it is the shared Redis L2 backplane.
        await cacheA.RemoveByTagAsync(tag);

        // Bounded poll: instance B should observe a distinguishable fresh value once (and only
        // once) the invalidation has propagated through the backplane and evicted B's stale L1
        // entry. A fixed Task.Delay would either flake under CI load (too short) or waste time
        // on every green run (too long); polling returns the moment propagation completes and
        // fails loudly, with the last-observed value, if it never does.
        var freshValue = "fresh-value-" + Guid.NewGuid();
        var observed = await PollUntilAsync(
            () => cacheB.GetOrSetAsync(key, async _ => freshValue, policy).AsTask(),
            value => value == freshValue,
            PollTimeout,
            PollInterval);

        Assert.Equal(freshValue, observed);
    }

    /// <summary>
    /// Polls <paramref name="poll"/> every <paramref name="interval"/> until
    /// <paramref name="isSatisfied"/> returns <see langword="true"/> or <paramref name="timeout"/>
    /// elapses. Fails the test with the last-observed value on timeout — never hangs
    /// indefinitely and never guesses a fixed delay.
    /// </summary>
    private static async Task<T> PollUntilAsync<T>(
        Func<Task<T>> poll,
        Func<T, bool> isSatisfied,
        TimeSpan timeout,
        TimeSpan interval)
    {
        var deadline = DateTime.UtcNow + timeout;
        T last;

        do
        {
            last = await poll();
            if (isSatisfied(last))
                return last;

            await Task.Delay(interval);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail(
            $"Cross-instance tag invalidation did not propagate within {timeout.TotalSeconds}s. " +
            $"Last observed value: '{last}'.");
        return last; // Unreachable — Assert.Fail throws.
    }
}
