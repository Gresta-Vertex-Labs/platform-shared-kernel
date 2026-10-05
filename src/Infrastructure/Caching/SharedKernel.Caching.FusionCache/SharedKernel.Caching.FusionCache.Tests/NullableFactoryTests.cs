using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Tests for the nullable overload of <see cref="ICacheService.GetOrSetAsync{T}"/>
/// (Phase 21 — VT-06, VT-07).
///
/// Verifies that:
/// <list type="bullet">
///   <item>A factory returning <see langword="null"/> is called only once on the first miss;
///   subsequent calls return <see langword="null"/> directly from cache without invoking the factory.</item>
///   <item>A factory returning a non-null value works correctly across a round-trip.</item>
/// </list>
/// </summary>
public sealed class NullableFactoryTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public NullableFactoryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "nullable-factory-test");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -----------------------------------------------------------------------
    // VT-06: null result is cached — factory called once on first miss
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the factory returns <see langword="null"/>, the null result is cached.
    /// A second request for the same key must not invoke the factory again.
    /// </summary>
    [Fact]
    public async Task NullableFactory_ReturningNull_CachesAbsence_FactoryCalledOnce()
    {
        var key = "nullable:null-result:" + Guid.NewGuid();
        var factoryCalls = 0;

        // Use a short but non-zero TTL — NeverExpire is explicitly prohibited for nullable null tests.
        var policy = CachePolicy.For(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // First call — cache miss; factory is invoked.
        var firstResult = await _cache.GetOrSetAsync<string?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<string?>(null);
            },
            policy);

        Assert.Null(firstResult);
        Assert.Equal(1, factoryCalls);

        // Second call — must be a cache hit; factory must NOT be invoked again.
        var secondResult = await _cache.GetOrSetAsync<string?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<string?>("should-never-be-returned");
            },
            policy);

        Assert.Null(secondResult);
        Assert.Equal(1, factoryCalls); // still 1 — factory not called on second request
    }

    /// <summary>
    /// Concurrent requests for the same uncached key with a null-returning factory:
    /// stampede protection ensures the factory is called at most once.
    /// </summary>
    [Fact]
    public async Task NullableFactory_ReturningNull_StampedeProtection_FactoryCalledOnce()
    {
        var key = "nullable:stampede-null:" + Guid.NewGuid();
        var factoryCalls = 0;
        var policy = CachePolicy.For(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            _cache.GetOrSetAsync<string?>(
                key,
                async ct =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(20, ct);
                    return (string?)null;
                },
                policy).AsTask());

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Null(r));
        Assert.Equal(1, factoryCalls);
    }

    // -----------------------------------------------------------------------
    // VT-07: non-null value round-trip via nullable overload
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the factory returns a non-null value via the nullable overload,
    /// the value is cached and returned correctly. The factory is called exactly once.
    /// </summary>
    [Fact]
    public async Task NullableFactory_ReturningValue_RoundTrip_FactoryCalledOnce()
    {
        var key = "nullable:value-result:" + Guid.NewGuid();
        var factoryCalls = 0;
        const string expected = "entity-payload";
        var policy = CachePolicy.For(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // First call — cache miss.
        var firstResult = await _cache.GetOrSetAsync<string?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<string?>(expected);
            },
            policy);

        Assert.Equal(expected, firstResult);
        Assert.Equal(1, factoryCalls);

        // Second call — must return cached value without invoking factory.
        var secondResult = await _cache.GetOrSetAsync<string?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<string?>("different-value");
            },
            policy);

        Assert.Equal(expected, secondResult);
        Assert.Equal(1, factoryCalls); // still 1 — factory not called on second request
    }

    /// <summary>
    /// The nullable overload with a value-type (int?) correctly caches zero
    /// (a falsy but non-null result) — confirming the overload handles value types too.
    /// </summary>
    [Fact]
    public async Task NullableFactory_ValueType_ZeroIsNotNull_CachedCorrectly()
    {
        var key = "nullable:value-type-zero:" + Guid.NewGuid();
        var factoryCalls = 0;
        var policy = CachePolicy.For(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        var firstResult = await _cache.GetOrSetAsync<int?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<int?>(0);
            },
            policy);

        Assert.Equal(0, firstResult);
        Assert.Equal(1, factoryCalls);

        // Zero must be returned from cache on the second call.
        var secondResult = await _cache.GetOrSetAsync<int?>(
            key,
            ct =>
            {
                Interlocked.Increment(ref factoryCalls);
                return ValueTask.FromResult<int?>(99);
            },
            policy);

        Assert.Equal(0, secondResult);
        Assert.Equal(1, factoryCalls);
    }
}
