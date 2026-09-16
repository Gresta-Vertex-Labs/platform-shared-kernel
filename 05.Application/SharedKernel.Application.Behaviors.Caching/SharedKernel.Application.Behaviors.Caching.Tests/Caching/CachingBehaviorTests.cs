using FluentAssertions;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Caching;

public sealed class CachingBehaviorTests
{
    private sealed record TestQuery(string Id) : IQuery<string>, ICacheableQuery<Result<string>>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => $"widget:{Id}";
    }

    [Fact]
    public async Task Handle_CacheMiss_InvokesFactoryOnceAndCachesSuccess()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);
        var nextCallCount = 0;

        var result = await behavior.Handle(new TestQuery("1"), () =>
        {
            nextCallCount++;
            return Task.FromResult(Result<string>.Success("widget-1"));
        }, CancellationToken.None);

        result.Value.Should().Be("widget-1");
        nextCallCount.Should().Be(1);
        cache.SetCalls.Should().ContainSingle().Which.Should().Be("widget:1");
    }

    [Fact]
    public async Task Handle_CacheHit_DoesNotInvokeFactory()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        await behavior.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        var nextCalled = false;
        var second = await behavior.Handle(new TestQuery("1"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("different"));
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        second.Value.Should().Be("widget-1");
    }

    [Fact]
    public async Task Handle_Failure_NeverCaches()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        var result = await behavior.Handle(
            new TestQuery("1"),
            () => Task.FromResult(Result<string>.Failure(Error.NotFound("test.not_found", "missing"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        cache.SetCalls.Should().BeEmpty();

        // A subsequent call must miss again — nothing was cached.
        var nextCalled = false;
        await behavior.Handle(new TestQuery("1"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Failure(Error.NotFound("test.not_found", "missing")));
        }, CancellationToken.None);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoRequestContextRegistered_UsesUnscopedKey()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        await behavior.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        cache.GetCalls.Should().Contain("widget:1");
        cache.SetCalls.Should().Contain("widget:1");
    }

    [Fact]
    public async Task Handle_DifferentTenants_ProduceDifferentCacheKeys_NeitherReadsTheOthers()
    {
        var cache = new FakeCacheService();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var behaviorA = new CachingBehavior<TestQuery, Result<string>>(cache, new FakeRequestContext(tenantA));
        var behaviorB = new CachingBehavior<TestQuery, Result<string>>(cache, new FakeRequestContext(tenantB));

        await behaviorA.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("a-value")), CancellationToken.None);

        var bCalledNext = false;
        var bResult = await behaviorB.Handle(new TestQuery("1"), () =>
        {
            bCalledNext = true;
            return Task.FromResult(Result<string>.Success("b-value"));
        }, CancellationToken.None);

        bCalledNext.Should().BeTrue("tenant B must not read tenant A's cache entry for the same logical key");
        bResult.Value.Should().Be("b-value");
        cache.SetCalls.Should().Contain($"tenant:{tenantA}:widget:1");
        cache.SetCalls.Should().Contain($"tenant:{tenantB}:widget:1");
    }

    [Fact]
    public async Task Handle_TenantScoped_TagsAreRewrittenPerTenant()
    {
        var cache = new FakeCacheService();
        var tenant = Guid.NewGuid();
        var behavior = new CachingBehavior<TaggedQuery, Result<string>>(cache, new FakeRequestContext(tenant));

        await behavior.Handle(new TaggedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.RemoveByTagCalls.Clear();
        await cache.RemoveByTagAsync($"tenant:{tenant}:widgets", CancellationToken.None);
        cache.GetCalls.Add("probe");
        (await cache.GetAsync<Result<string>>($"tenant:{tenant}:widget:tagged", CancellationToken.None)).Should().BeNull("the tag-scoped eviction must have removed the entry");
    }

    private sealed record TaggedQuery : IQuery<string>, ICacheableQuery<Result<string>>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "widget:tagged";
    }

    [Fact]
    public void ACommandType_CanNeverSatisfyICacheableQuery_ContractShapeOnly()
    {
        // Contract-shape assertion only — CachingBehavior<TRequest,TResponse> is constrained to
        // IQueryBase, which ICommand/ICommand<TResponse> never implement, so no command type can
        // ever be accepted at this generic parameter; there is no reachable runtime case to test.
        typeof(ICommand).Should().NotBeAssignableTo<IQueryBase>();
        typeof(TestQuery).Should().BeAssignableTo<IQueryBase>();
        typeof(TestQuery).Should().BeAssignableTo<ICacheableQuery<Result<string>>>();
    }
}
