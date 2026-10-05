using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="ICacheService.TryGetManyAsync{T}"/> and
/// <see cref="ICacheService.SetManyAsync{T}"/> batch operations.
/// </summary>
public sealed class BatchOperationsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public BatchOperationsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // TryGetManyAsync — empty input
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetManyAsync_EmptyKeyList_ReturnsEmptyDictionary()
    {
        var result = await _cache.TryGetManyAsync<string>([], CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task TryGetManyAsync_NullKeys_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _cache.TryGetManyAsync<string>(null!, CancellationToken.None).AsTask());
    }

    // -------------------------------------------------------------------------
    // TryGetManyAsync — all misses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetManyAsync_AllMisses_ReturnsMissPerKey()
    {
        var keys = new[]
        {
            "batch:miss:1-" + Guid.NewGuid(),
            "batch:miss:2-" + Guid.NewGuid(),
            "batch:miss:3-" + Guid.NewGuid()
        };

        var result = await _cache.TryGetManyAsync<string>(keys, CancellationToken.None);

        // Every key must be present even on miss.
        Assert.Equal(keys.Length, result.Count);
        foreach (var key in keys)
        {
            Assert.True(result.ContainsKey(key));
            Assert.False(result[key].IsHit);
        }
    }

    // -------------------------------------------------------------------------
    // TryGetManyAsync — mixed hits and misses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetManyAsync_MixedHitsAndMisses_ReturnsCorrectValuesPerKey()
    {
        var hitKey1 = "batch:hit:1-" + Guid.NewGuid();
        var hitKey2 = "batch:hit:2-" + Guid.NewGuid();
        var missKey = "batch:miss:x-" + Guid.NewGuid();

        await _cache.SetAsync(hitKey1, "alpha", CachePolicy.Default);
        await _cache.SetAsync(hitKey2, "beta", CachePolicy.Default);

        var keys = new[] { hitKey1, missKey, hitKey2 };
        var result = await _cache.TryGetManyAsync<string>(keys, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("alpha", result[hitKey1].Value);
        Assert.Equal("beta", result[hitKey2].Value);
        Assert.False(result[missKey].IsHit);
    }

    // -------------------------------------------------------------------------
    // SetManyAsync then TryGetManyAsync — round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetManyAsync_ThenTryGetManyAsync_ReturnsAllStoredValues()
    {
        var suffix = Guid.NewGuid().ToString();
        var entries = new Dictionary<string, string>
        {
            [$"batch:set:a-{suffix}"] = "value-a",
            [$"batch:set:b-{suffix}"] = "value-b",
            [$"batch:set:c-{suffix}"] = "value-c"
        };

        await _cache.SetManyAsync(entries, CachePolicy.Default, CancellationToken.None);

        var result = await _cache.TryGetManyAsync<string>(entries.Keys, CancellationToken.None);

        Assert.Equal(entries.Count, result.Count);
        foreach (var (key, expectedValue) in entries)
            Assert.Equal(expectedValue, result[key].Value);
    }

    // -------------------------------------------------------------------------
    // SetManyAsync — empty entries is a no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetManyAsync_EmptyEntries_IsNoOp()
    {
        var ex = await Record.ExceptionAsync(() =>
            _cache.SetManyAsync(
                new Dictionary<string, string>(),
                CachePolicy.Default,
                CancellationToken.None).AsTask());

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // SetManyAsync — null guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetManyAsync_NullEntries_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _cache.SetManyAsync<string>(null!, CachePolicy.Default, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task SetManyAsync_NullPolicy_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _cache.SetManyAsync(
                new Dictionary<string, string> { ["k"] = "v" },
                null!,
                CancellationToken.None).AsTask());
    }

    // -------------------------------------------------------------------------
    // TryGetManyAsync — every input key has a dictionary entry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetManyAsync_SingleKey_MissReturnsMissNotMissingEntry()
    {
        var key = "batch:single-miss-" + Guid.NewGuid();

        var result = await _cache.TryGetManyAsync<int>(new[] { key }, CancellationToken.None);

        Assert.Single(result);
        Assert.True(result.ContainsKey(key));
        Assert.False(result[key].IsHit);
    }

    [Fact]
    public async Task TryGetManyAsync_DuplicateKeys_ReturnsOneLookupPerDistinctKey()
    {
        var suffix = Guid.NewGuid().ToString();
        var hitKey = $"batch:distinct:hit-{suffix}";
        var nullKey = $"batch:distinct:null-{suffix}";
        var missKey = $"batch:distinct:miss-{suffix}";

        await _cache.SetAsync(hitKey, "value", CachePolicy.Default);
        await _cache.SetAsync<string?>(nullKey, null, CachePolicy.Default);

        var result = await _cache.TryGetManyAsync<string?>(
            [hitKey, missKey, hitKey, nullKey, missKey],
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(CacheLookup<string?>.Hit("value"), result[hitKey]);
        Assert.True(result[nullKey].IsHit);
        Assert.Null(result[nullKey].Value);
        Assert.False(result[missKey].IsHit);
    }

    // -------------------------------------------------------------------------
    // SetManyAsync — single CachePolicy applies to all entries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetManyAsync_WithTag_AllEntriesEvictedByTag()
    {
        const string tag = "batch-tag-eviction";
        var policy = CachePolicy.Default.WithTags(tag);
        var suffix = Guid.NewGuid().ToString();

        var entries = new Dictionary<string, int>
        {
            [$"batch:tagged:1-{suffix}"] = 1,
            [$"batch:tagged:2-{suffix}"] = 2
        };

        await _cache.SetManyAsync(entries, policy, CancellationToken.None);

        // Verify they exist before tag eviction.
        var before = await _cache.TryGetManyAsync<int>(entries.Keys, CancellationToken.None);
        Assert.Equal(1, before[$"batch:tagged:1-{suffix}"].Value);
        Assert.Equal(2, before[$"batch:tagged:2-{suffix}"].Value);

        // Evict by tag.
        await _cache.RemoveByTagAsync(tag);

        var after = await _cache.TryGetManyAsync<int>(entries.Keys, CancellationToken.None);
        Assert.False(after[$"batch:tagged:1-{suffix}"].IsHit);
        Assert.False(after[$"batch:tagged:2-{suffix}"].IsHit);
    }

    // -------------------------------------------------------------------------
    // BP-05: stampede protection under concurrency — Phase 40 rewrote
    // TryGetManyAsync/SetManyAsync to run concurrently via Parallel.ForEachAsync
    // over a ConcurrentDictionary accumulator. This proves that change
    // introduces no shared mutable state that could interfere with
    // FusionCache's own per-key stampede-protection lock inside GetOrSetAsync.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_StampedeProtection_UnaffectedByInterleavedBatchOperations()
    {
        var stampedeKey = "test:stampede-interleaved-" + Guid.NewGuid();
        var factoryCalls = 0;

        // Fire 20 concurrent requests for the same uncached key...
        const int concurrency = 20;
        var stampedeTasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrSetAsync(
                stampedeKey,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, ct); // simulate work
                    return "stampede-interleaved-value";
                },
                CachePolicy.Default).AsTask());

        // ...interleaved with an unrelated batch call over more keys than
        // MaxDegreeOfParallelism (16), so its own internal Parallel.ForEachAsync
        // fan-out overlaps in wall-clock time with the stampede burst above.
        var suffix = Guid.NewGuid().ToString();
        var batchEntries = Enumerable.Range(0, 20)
            .ToDictionary(i => $"batch:interleaved:{i}-{suffix}", i => i);
        var setManyTask = _cache.SetManyAsync(batchEntries, CachePolicy.Default, CancellationToken.None).AsTask();
        var getManyTask = _cache.TryGetManyAsync<int>(batchEntries.Keys, CancellationToken.None).AsTask();

        var results = await Task.WhenAll(stampedeTasks);
        await setManyTask;
        await getManyTask;

        // All stampede results must be the same value.
        Assert.All(results, r => Assert.Equal("stampede-interleaved-value", r));

        // The factory must have been invoked exactly once thanks to FusionCache
        // stampede protection — unaffected by the concurrently-running batch calls.
        Assert.Equal(1, factoryCalls);
    }

    // -------------------------------------------------------------------------
    // BP-08: explicit regression coverage for Phase 22 contracts, re-affirmed
    // unchanged after Phase 40's bounded-concurrency rewrite.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetManyAsync_EmptyKeyList_UnderBoundedConcurrency_StillShortCircuitsToEmptyDictionary()
    {
        // Phase 22 contract: an empty input enumerable returns an empty dictionary.
        // Phase 40 rewrote the loop body to Parallel.ForEachAsync over an empty
        // source, which completes immediately with zero scheduled iterations —
        // confirm this explicitly rather than relying on incidental behavior.
        var result = await _cache.TryGetManyAsync<string>(Array.Empty<string>(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetManyAsync_UnderBoundedConcurrency_SinglePolicyStillAppliesToEveryEntry()
    {
        // Phase 22 contract: a single CachePolicy applies to all entries in the batch.
        // Confirm every entry — not just a subset — still receives the same policy's
        // tag under Phase 40's concurrent execution model, so RemoveByTagAsync evicts
        // the entire batch, not a partial set.
        const string tag = "batch-tag-single-policy-regression";
        var policy = CachePolicy.Default.WithTags(tag);
        var suffix = Guid.NewGuid().ToString();

        var entries = new Dictionary<string, int>
        {
            [$"batch:single-policy:1-{suffix}"] = 1,
            [$"batch:single-policy:2-{suffix}"] = 2,
            [$"batch:single-policy:3-{suffix}"] = 3
        };

        await _cache.SetManyAsync(entries, policy, CancellationToken.None);

        var before = await _cache.TryGetManyAsync<int>(entries.Keys, CancellationToken.None);
        foreach (var (key, value) in entries)
            Assert.Equal(value, before[key].Value);

        await _cache.RemoveByTagAsync(tag);

        var after = await _cache.TryGetManyAsync<int>(entries.Keys, CancellationToken.None);
        foreach (var key in entries.Keys)
            Assert.False(after[key].IsHit);
    }
}
