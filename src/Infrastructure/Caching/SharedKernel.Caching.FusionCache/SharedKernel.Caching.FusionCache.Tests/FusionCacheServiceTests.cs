using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="FusionCacheService"/> using an in-process FusionCache instance.
/// Covers lookups, set, remove, expire, tag eviction, clear, factory decisions, and stampede protection.
/// </summary>
public sealed class FusionCacheServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public FusionCacheServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    private static string NewKey(string name) => $"test:{name}:{Guid.NewGuid():N}";

    // -------------------------------------------------------------------------
    // TryGetAsync / SetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ThenTryGetAsync_ReturnsStoredValue()
    {
        var key = NewKey("set-get");

        await _cache.SetAsync(key, "hello-world", CachePolicy.Default);
        var result = await _cache.TryGetAsync<string>(key);

        Assert.True(result.IsHit);
        Assert.Equal("hello-world", result.Value);
    }

    [Fact]
    public async Task TryGetAsync_UnknownKey_IsMiss()
    {
        var result = await _cache.TryGetAsync<string>(NewKey("nonexistent"));

        Assert.False(result.IsHit);
        Assert.Equal(CacheLookup<string>.Miss, result);
    }

    [Fact]
    public async Task TryGetAsync_DistinguishesMiss_FromCachedNull_AndCachedZero()
    {
        var nullKey = NewKey("cached-null");
        var zeroKey = NewKey("cached-zero");
        var missKey = NewKey("miss");

        await _cache.SetAsync<string?>(nullKey, null, CachePolicy.Default);
        await _cache.SetAsync(zeroKey, 0, CachePolicy.Default);

        var cachedNull = await _cache.TryGetAsync<string?>(nullKey);
        var cachedZero = await _cache.TryGetAsync<int>(zeroKey);
        var missString = await _cache.TryGetAsync<string?>(missKey);
        var missInt = await _cache.TryGetAsync<int>(missKey);

        Assert.True(cachedNull.IsHit);
        Assert.Null(cachedNull.Value);
        Assert.True(cachedZero.IsHit);
        Assert.Equal(0, cachedZero.Value);
        Assert.False(missString.IsHit);
        Assert.False(missInt.IsHit);
        Assert.Equal(-1, missInt.GetValueOrDefault(-1));
    }

    [Fact]
    public async Task TryGetAsync_AfterGetOrSetReturningNull_IsHitWithNull()
    {
        var key = NewKey("gos-null");

        await _cache.GetOrSetAsync<string?>(key, _ => ValueTask.FromResult<string?>(null), CachePolicy.Default);
        var result = await _cache.TryGetAsync<string?>(key);

        Assert.True(result.IsHit);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task TryGetAsync_NullOrWhitespaceKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.TryGetAsync<string>("").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.TryGetAsync<string>("   ").AsTask());
    }

    // -------------------------------------------------------------------------
    // RemoveAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_ExistingKey_IsMissAfterRemoval()
    {
        var key = NewKey("remove");
        await _cache.SetAsync(key, 42, CachePolicy.Default);

        await _cache.RemoveAsync(key);

        Assert.False((await _cache.TryGetAsync<int>(key)).IsHit);
    }

    [Fact]
    public async Task RemoveAsync_FailSafePolicy_FactoryFailureCannotServeRemovedValue()
    {
        var key = NewKey("remove-no-failsafe");
        var policy = CachePolicy.Default.WithFailSafe(TimeSpan.FromMinutes(10));
        await _cache.SetAsync(key, "old", policy);

        await _cache.RemoveAsync(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cache.GetOrSetAsync<string>(key, _ => throw new InvalidOperationException("source down"), policy).AsTask());
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        var ex = await Record.ExceptionAsync(() => _cache.RemoveAsync(NewKey("does-not-exist")).AsTask());

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // ExpireAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExpireAsync_NextGetOrSetRecomputes()
    {
        var key = NewKey("expire-recompute");
        var policy = CachePolicy.Default.WithFailSafe(TimeSpan.FromMinutes(10));
        await _cache.SetAsync(key, "old", policy);

        await _cache.ExpireAsync(key);

        var factoryCalls = 0;
        var result = await _cache.GetOrSetAsync(
            key,
            _ => { factoryCalls++; return ValueTask.FromResult("new"); },
            policy);

        Assert.Equal("new", result);
        Assert.Equal(1, factoryCalls);
        Assert.Equal("new", (await _cache.TryGetAsync<string>(key)).Value);
    }

    [Fact]
    public async Task ExpireAsync_FailSafePolicy_FactoryFailureServesOldValue()
    {
        var key = NewKey("expire-failsafe");
        var policy = CachePolicy.Default.WithFailSafe(TimeSpan.FromMinutes(10));
        await _cache.SetAsync(key, "old", policy);

        await _cache.ExpireAsync(key);

        var result = await _cache.GetOrSetAsync<string>(
            key,
            _ => throw new InvalidOperationException("source down"),
            policy);

        Assert.Equal("old", result);
    }

    [Fact]
    public async Task ExpireAsync_NullOrWhitespaceKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.ExpireAsync(" ").AsTask());
    }

    // -------------------------------------------------------------------------
    // GetOrSetAsync — value and stampede protection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_InvokesFactoryAndCachesResult()
    {
        var key = NewKey("get-or-set");
        var factoryCalls = 0;

        var result = await _cache.GetOrSetAsync(
            key,
            _ => { factoryCalls++; return ValueTask.FromResult("factory-value"); },
            CachePolicy.Default);

        Assert.Equal("factory-value", result);
        Assert.Equal(1, factoryCalls);
        Assert.Equal("factory-value", (await _cache.TryGetAsync<string>(key)).Value);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_DoesNotInvokeFactory()
    {
        var key = NewKey("get-or-set-hit");
        await _cache.SetAsync(key, "cached", CachePolicy.Default);

        var factoryCalls = 0;
        var result = await _cache.GetOrSetAsync(
            key,
            _ => { factoryCalls++; return ValueTask.FromResult("fresh"); },
            CachePolicy.Default);

        Assert.Equal("cached", result);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_StampedeProtection_FactoryCalledExactlyOnce()
    {
        var key = NewKey("stampede");
        var factoryCalls = 0;

        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrSetAsync(
                key,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, ct);
                    return "stampede-value";
                },
                CachePolicy.Default).AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("stampede-value", r));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ContextOverload_ReceivesKeyAndPolicy()
    {
        var key = NewKey("context");
        var policy = CachePolicy.For(TimeSpan.FromMinutes(2)).WithTags("ctx");
        CacheFactoryContext? captured = null;

        await _cache.GetOrSetAsync(
            key,
            (context, _) => { captured = context; return ValueTask.FromResult(1); },
            policy);

        Assert.NotNull(captured);
        Assert.Equal(key, captured.Key);
        Assert.Equal(policy, captured.Policy);
    }

    // -------------------------------------------------------------------------
    // GetOrSetAsync — factory decisions through CacheFactoryContext
    // -------------------------------------------------------------------------

    public static TheoryData<string> SkipCachingPolicies => new() { "default", "without-fail-safe", "local-only" };

    [Theory]
    [MemberData(nameof(SkipCachingPolicies))]
    public async Task GetOrSetAsync_SkipCaching_ReturnsValue_ButStoresNothing(string policyName)
    {
        var policy = policyName switch
        {
            "without-fail-safe" => CachePolicy.Default.WithoutFailSafe(),
            "local-only" => CachePolicy.Default.LocalOnly(),
            _ => CachePolicy.Default,
        };

        var key = NewKey("skip-caching");
        var factoryCalls = 0;

        ValueTask<string> Factory(CacheFactoryContext context, CancellationToken _)
        {
            var call = Interlocked.Increment(ref factoryCalls);
            context.SkipCaching();
            return ValueTask.FromResult($"value-{call}");
        }

        var first = await _cache.GetOrSetAsync<string>(key, Factory, policy);
        var lookup = await _cache.TryGetAsync<string>(key);
        var second = await _cache.GetOrSetAsync<string>(key, Factory, policy);

        Assert.Equal("value-1", first);
        Assert.False(lookup.IsHit);
        Assert.Equal("value-2", second);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_SkipCachingOnlyForSomeCalls_CachesTheOthers()
    {
        var key = NewKey("skip-then-cache");
        var factoryCalls = 0;

        ValueTask<string> Factory(CacheFactoryContext context, CancellationToken _)
        {
            var call = Interlocked.Increment(ref factoryCalls);
            if (call == 1)
                context.SkipCaching();

            return ValueTask.FromResult($"value-{call}");
        }

        Assert.Equal("value-1", await _cache.GetOrSetAsync<string>(key, Factory, CachePolicy.Default));
        Assert.Equal("value-2", await _cache.GetOrSetAsync<string>(key, Factory, CachePolicy.Default));
        Assert.Equal("value-2", await _cache.GetOrSetAsync<string>(key, Factory, CachePolicy.Default));
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_SetDurations_ShortOverrideExpires_WhilePolicyDurationEntrySurvives()
    {
        var policy = CachePolicy.For(TimeSpan.FromHours(1)).WithoutFailSafe().WithoutEagerRefresh();
        var overriddenKey = NewKey("duration-override");
        var controlKey = NewKey("duration-control");

        await _cache.GetOrSetAsync(
            overriddenKey,
            (context, _) =>
            {
                context.SetDurations(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
                return ValueTask.FromResult("short-lived");
            },
            policy);
        await _cache.GetOrSetAsync(controlKey, _ => ValueTask.FromResult("long-lived"), policy);

        Assert.True((await _cache.TryGetAsync<string>(overriddenKey)).IsHit);

        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.False((await _cache.TryGetAsync<string>(overriddenKey)).IsHit);
        Assert.Equal("long-lived", (await _cache.TryGetAsync<string>(controlKey)).Value);

        var recomputed = await _cache.GetOrSetAsync(overriddenKey, _ => ValueTask.FromResult("recomputed"), policy);
        Assert.Equal("recomputed", recomputed);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryThrows_NothingCached_AndExceptionPropagates()
    {
        var key = NewKey("factory-throws");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cache.GetOrSetAsync<string>(key, _ => throw new InvalidOperationException("boom"), CachePolicy.Default).AsTask());

        Assert.False((await _cache.TryGetAsync<string>(key)).IsHit);
    }

    // -------------------------------------------------------------------------
    // Tag-based eviction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByTagAsync_EvictsAllEntriesWithTag()
    {
        var tag = "test-tag-" + Guid.NewGuid();
        var policy = CachePolicy.Default.WithTags(tag);
        var tagged1 = NewKey("tagged");
        var tagged2 = NewKey("tagged");
        var untagged = NewKey("untagged");

        await _cache.SetAsync(tagged1, "a", policy);
        await _cache.SetAsync(tagged2, "b", policy);
        await _cache.SetAsync(untagged, "c", CachePolicy.Default);

        await _cache.RemoveByTagAsync(tag);

        Assert.False((await _cache.TryGetAsync<string>(tagged1)).IsHit);
        Assert.False((await _cache.TryGetAsync<string>(tagged2)).IsHit);
        Assert.Equal("c", (await _cache.TryGetAsync<string>(untagged)).Value);
    }

    [Fact]
    public async Task RemoveByTagsAsync_EvictsEntriesCarryingAnyOfTheTags_AndLeavesOthers()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (tagA, tagB, tagC) = ($"a-{suffix}", $"b-{suffix}", $"c-{suffix}");
        var keyA = NewKey("tags-a");
        var keyB = NewKey("tags-b");
        var keyAc = NewKey("tags-ac");
        var keyC = NewKey("tags-c");
        var keyUntagged = NewKey("tags-none");

        await _cache.SetAsync(keyA, "a", CachePolicy.Default.WithTags(tagA));
        await _cache.SetAsync(keyB, "b", CachePolicy.Default.WithTags(tagB));
        await _cache.SetAsync(keyAc, "ac", CachePolicy.Default.WithTags(tagA, tagC));
        await _cache.SetAsync(keyC, "c", CachePolicy.Default.WithTags(tagC));
        await _cache.SetAsync(keyUntagged, "none", CachePolicy.Default);

        await _cache.RemoveByTagsAsync([tagA, tagB, tagA]);

        Assert.False((await _cache.TryGetAsync<string>(keyA)).IsHit);
        Assert.False((await _cache.TryGetAsync<string>(keyB)).IsHit);
        Assert.False((await _cache.TryGetAsync<string>(keyAc)).IsHit);
        Assert.Equal("c", (await _cache.TryGetAsync<string>(keyC)).Value);
        Assert.Equal("none", (await _cache.TryGetAsync<string>(keyUntagged)).Value);
    }

    [Fact]
    public async Task RemoveByTagsAsync_EmptySequence_IsNoOp()
    {
        var key = NewKey("tags-empty");
        await _cache.SetAsync(key, "kept", CachePolicy.Default.WithTags("kept-tag"));

        await _cache.RemoveByTagsAsync([]);

        Assert.Equal("kept", (await _cache.TryGetAsync<string>(key)).Value);
    }

    [Fact]
    public async Task RemoveByTagsAsync_InvalidArguments_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _cache.RemoveByTagsAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.RemoveByTagsAsync(["ok", " "]).AsTask());
    }

    [Fact]
    public async Task RemoveByTagAsync_NullOrWhitespaceTag_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.RemoveByTagAsync("").AsTask());
    }

    // -------------------------------------------------------------------------
    // ClearAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_RemovesEveryEntry()
    {
        var plain = NewKey("clear-plain");
        var tagged = NewKey("clear-tagged");
        var failSafe = NewKey("clear-failsafe");
        var failSafePolicy = CachePolicy.Default.WithFailSafe(TimeSpan.FromMinutes(10));

        await _cache.SetAsync(plain, "p", CachePolicy.Default);
        await _cache.SetAsync(tagged, "t", CachePolicy.Default.WithTags("clear-tag"));
        await _cache.SetAsync(failSafe, "old", failSafePolicy);

        await _cache.ClearAsync();

        Assert.False((await _cache.TryGetAsync<string>(plain)).IsHit);
        Assert.False((await _cache.TryGetAsync<string>(tagged)).IsHit);

        // Cleared without fail-safe: a failing factory cannot resurrect the old value.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _cache.GetOrSetAsync<string>(failSafe, _ => throw new InvalidOperationException("down"), failSafePolicy).AsTask());
    }
}
