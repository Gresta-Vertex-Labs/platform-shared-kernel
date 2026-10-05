using SharedKernel.Application.Caching;
using FluentAssertions;
using SharedKernel.Application.Pipeline.Caching.Tests.Support;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Caching.Tests.Caching;

/// <summary>
/// Covers the two opt-ins on <see cref="ICacheableQuery"/>: forcing a refresh, and declining to
/// cache a particular value.
/// </summary>
public sealed class CachingBehaviorRefreshAndPredicateTests
{
    private sealed record RefreshableQuery(bool RefreshCache) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "k";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record EmptyRejectingQuery : ICacheableQuery<IReadOnlyList<string>>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "k";
        public CacheScope Scope => CacheScope.Global;

        public bool ShouldCache(IReadOnlyList<string> value) => value.Count > 0;
    }

    [Fact]
    public async Task Handle_RefreshCache_RunsTheHandlerEvenWhenAnEntryExists()
    {
        var cache = new FakeCacheService();
        var normal = TestPipeline.Caching<RefreshableQuery, Result<string>>(cache);

        await normal.Handle(new RefreshableQuery(false), () => Task.FromResult(Result<string>.Success("old")), CancellationToken.None);

        var refreshHandlerRan = false;
        var refreshed = await normal.Handle(new RefreshableQuery(true), () =>
        {
            refreshHandlerRan = true;
            return Task.FromResult(Result<string>.Success("new"));
        }, CancellationToken.None);

        refreshHandlerRan.Should().BeTrue();
        refreshed.Value.Should().Be("new");
    }

    [Fact]
    public async Task Handle_RefreshCache_OverwritesTheEntryForTheNextOrdinaryCaller()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<RefreshableQuery, Result<string>>(cache);

        await behavior.Handle(new RefreshableQuery(false), () => Task.FromResult(Result<string>.Success("old")), CancellationToken.None);
        await behavior.Handle(new RefreshableQuery(true), () => Task.FromResult(Result<string>.Success("new")), CancellationToken.None);

        var handlerRan = false;
        var served = await behavior.Handle(new RefreshableQuery(false), () =>
        {
            handlerRan = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }, CancellationToken.None);

        handlerRan.Should().BeFalse("the refresh must have written the entry, not merely bypassed it");
        served.Value.Should().Be("new");
    }

    [Fact]
    public async Task Handle_RefreshCache_KeepsTheQueryPolicyTags()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<RefreshableQuery, Result<string>>(cache);

        await behavior.Handle(new RefreshableQuery(true), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.GetTags(TestKeys.Global(nameof(RefreshableQuery), "k")).Should().Equal("widgets");
    }

    [Fact]
    public async Task Handle_RefreshCacheWithAFailure_LeavesTheExistingEntryIntact()
    {
        // A failed refresh is not evidence the cached value is wrong; dropping it would turn a
        // transient fault into a miss storm.
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<RefreshableQuery, Result<string>>(cache);

        await behavior.Handle(new RefreshableQuery(false), () => Task.FromResult(Result<string>.Success("old")), CancellationToken.None);

        var failed = await behavior.Handle(
            new RefreshableQuery(true),
            () => Task.FromResult(Result<string>.Failure(Error.Unexpected("refresh.failed", "upstream down"))),
            CancellationToken.None);

        failed.IsFailure.Should().BeTrue();

        var handlerRan = false;
        var served = await behavior.Handle(new RefreshableQuery(false), () =>
        {
            handlerRan = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }, CancellationToken.None);

        handlerRan.Should().BeFalse();
        served.Value.Should().Be("old");
    }

    [Fact]
    public async Task Handle_ShouldCacheRejectsTheValue_ReturnsItButCachesNothing()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<EmptyRejectingQuery, Result<IReadOnlyList<string>>>(cache);

        var result = await behavior.Handle(
            new EmptyRejectingQuery(),
            () => Task.FromResult(Result<IReadOnlyList<string>>.Success([])),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        cache.Keys.Should().BeEmpty();
        cache.SetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCacheAcceptsTheValue_CachesAsNormal()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<EmptyRejectingQuery, Result<IReadOnlyList<string>>>(cache);

        await behavior.Handle(
            new EmptyRejectingQuery(),
            () => Task.FromResult(Result<IReadOnlyList<string>>.Success(["a"])),
            CancellationToken.None);

        cache.Keys.Should().Equal(TestKeys.Global(nameof(EmptyRejectingQuery), "k"));
    }

    [Fact]
    public async Task Handle_ShouldCacheRejectsTheValue_LeavesAnExistingEntryUntouched()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<EmptyRejectingQuery, Result<IReadOnlyList<string>>>(cache);

        await behavior.Handle(
            new EmptyRejectingQuery(),
            () => Task.FromResult(Result<IReadOnlyList<string>>.Success(["a"])),
            CancellationToken.None);

        // A hit never reaches the predicate; assert the stored entry survives a later rejected run
        // by removing it and re-running with an empty value.
        await cache.RemoveAsync(TestKeys.Global(nameof(EmptyRejectingQuery), "k"));
        await behavior.Handle(
            new EmptyRejectingQuery(),
            () => Task.FromResult(Result<IReadOnlyList<string>>.Success([])),
            CancellationToken.None);

        cache.Keys.Should().BeEmpty();
    }
}
