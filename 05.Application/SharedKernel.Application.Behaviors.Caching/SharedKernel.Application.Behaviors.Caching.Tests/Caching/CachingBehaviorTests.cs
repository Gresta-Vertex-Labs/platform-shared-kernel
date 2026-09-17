using FluentAssertions;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Caching;

public sealed class CachingBehaviorTests
{
    private sealed record TestQuery(string Id) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => $"widget:{Id}";
    }

    private sealed record TaggedQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "widget:tagged";
    }

    private sealed record ConfiguredPolicyQuery(CachePolicy CachePolicy) : ICacheableQuery<string>
    {
        public string CacheKey => "widget:configured";
    }

    private sealed record MismatchedQuery : ICacheableQuery<string>, MediatR.IRequest<int>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "widget:mismatched";
    }

    private sealed record RawKeyQuery(string CacheKey) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
    }

    [Fact]
    public async Task Handle_CacheMiss_InvokesNextOnceAndCachesSuccess()
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
    public async Task Handle_CacheHit_ServesCachedValueWithoutInvokingNext()
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
    public async Task Handle_Success_CachesTheValueNotTheResult()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        await behavior.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        (await cache.TryGetAsync<string>("widget:1")).TryGetValue(out var cached).Should().BeTrue();
        cached.Should().Be("widget-1");
        (await cache.TryGetAsync<Result<string>>("widget:1")).IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ResponseIsNotTheQueryResult_ThrowsWithoutInvokingNext()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<MismatchedQuery, int>(cache);
        var nextCalled = false;

        var act = async () => await behavior.Handle(new MismatchedQuery(), () =>
        {
            nextCalled = true;
            return Task.FromResult(1);
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*must return Result<String>*");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Failure_ReturnedButNeverCached()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        var result = await behavior.Handle(
            new TestQuery("1"),
            () => Task.FromResult(Result<string>.Failure(Error.NotFound("test.not_found", "missing"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("test.not_found");
        cache.SetCalls.Should().BeEmpty();
        cache.Keys.Should().BeEmpty();

        // A subsequent call must miss again — nothing was cached.
        var nextCalled = false;
        var second = await behavior.Handle(new TestQuery("1"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("recovered"));
        }, CancellationToken.None);
        nextCalled.Should().BeTrue();
        second.Value.Should().Be("recovered");
    }

    [Fact]
    public async Task Handle_UsesSingleContextAwareGetOrSet_NotReadThenWrite()
    {
        // Stampede protection lives in the cache service's GetOrSetAsync; the behavior must route
        // the handler through it as one call rather than a separate read followed by a write.
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache);

        await behavior.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        cache.ContextGetOrSetCalls.Should().ContainSingle().Which.Key.Should().Be("widget:1");
        cache.PlainGetOrSetCalls.Should().BeEmpty();
        cache.TryGetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PolicyDisablesEagerRefreshAndFactoryTimeouts_PreservesEverythingElse()
    {
        var cache = new FakeCacheService();
        var configured = CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
            .WithTags("widgets")
            .WithFailSafe(TimeSpan.FromHours(1))
            .WithFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2))
            .WithEagerRefresh(0.8)
            .WithJitter(TimeSpan.FromSeconds(5));
        var behavior = new CachingBehavior<ConfiguredPolicyQuery, Result<string>>(cache);

        await behavior.Handle(new ConfiguredPolicyQuery(configured), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        var used = cache.ContextGetOrSetCalls.Should().ContainSingle().Subject.Policy;
        used.EagerRefreshThreshold.Should().BeNull();
        used.FactorySoftTimeout.Should().BeNull();
        used.FactoryHardTimeout.Should().BeNull();
        used.L1Duration.Should().Be(TimeSpan.FromMinutes(1));
        used.L2Duration.Should().Be(TimeSpan.FromMinutes(10));
        used.Tags.Should().Equal("widgets");
        used.IsFailSafeEnabled.Should().BeTrue();
        used.FailSafeMaxDuration.Should().Be(TimeSpan.FromHours(1));
        used.JitterMaxDuration.Should().Be(TimeSpan.FromSeconds(5));
        used.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoRequestContextRegistered_UsesUnscopedKeyAndTags()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TaggedQuery, Result<string>>(cache);

        await behavior.Handle(new TaggedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.SetCalls.Should().Equal("widget:tagged");
        cache.GetTags("widget:tagged").Should().Equal("widgets");
    }

    [Fact]
    public async Task Handle_RequestContextWithoutTenant_UsesUnscopedKey()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(cache, new FakeRequestContext(tenantId: null));

        await behavior.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        cache.SetCalls.Should().Equal("widget:1");
        cache.ContextGetOrSetCalls.Single().Policy.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TenantScoped_KeyAndPolicyUseTenantFormat()
    {
        var cache = new FakeCacheService();
        var tenant = Guid.NewGuid();
        var tenantId = tenant.ToString("D");
        var behavior = new CachingBehavior<TaggedQuery, Result<string>>(cache, new FakeRequestContext(tenant));

        await behavior.Handle(new TaggedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        var (key, policy) = cache.ContextGetOrSetCalls.Should().ContainSingle().Subject;
        key.Should().Be(CacheKeyFormat.BuildTenantTag(tenantId, "widget:tagged"));
        key.Should().Be($"@{tenantId}:widget%3Atagged");
        policy.IsTenantScoped.Should().BeTrue();
        policy.Tags.Should().Equal($"@{tenantId}:widgets", $"@{tenantId}");
        cache.GetTags(key).Should().Equal($"@{tenantId}:widgets", $"@{tenantId}");
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
        cache.SetCalls.Should().Equal(
            CacheKeyFormat.BuildTenantTag(tenantA.ToString("D"), "widget:1"),
            CacheKeyFormat.BuildTenantTag(tenantB.ToString("D"), "widget:1"));

        var aAgain = await behaviorA.Handle(new TestQuery("1"), () => Task.FromResult(Result<string>.Success("unexpected")), CancellationToken.None);
        aAgain.Value.Should().Be("a-value");
    }

    [Fact]
    public async Task Handle_TenantScopedTagEviction_RemovesOnlyThatTenantsEntry()
    {
        var cache = new FakeCacheService();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var behaviorA = new CachingBehavior<TaggedQuery, Result<string>>(cache, new FakeRequestContext(tenantA));
        var behaviorB = new CachingBehavior<TaggedQuery, Result<string>>(cache, new FakeRequestContext(tenantB));

        await behaviorA.Handle(new TaggedQuery(), () => Task.FromResult(Result<string>.Success("a")), CancellationToken.None);
        await behaviorB.Handle(new TaggedQuery(), () => Task.FromResult(Result<string>.Success("b")), CancellationToken.None);

        await cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantTag(tenantA.ToString("D"), "widgets"));

        (await cache.TryGetAsync<string>(CacheKeyFormat.BuildTenantTag(tenantA.ToString("D"), "widget:tagged"))).IsHit.Should().BeFalse();
        (await cache.TryGetAsync<string>(CacheKeyFormat.BuildTenantTag(tenantB.ToString("D"), "widget:tagged"))).IsHit.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_GlobalKeyStartingWithTenantMarker_ThrowsWithoutInvokingNext()
    {
        var cache = new FakeCacheService();
        var behavior = new CachingBehavior<RawKeyQuery, Result<string>>(cache);
        var nextCalled = false;

        var act = async () => await behavior.Handle(new RawKeyQuery("@spoofed:widget"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result<string>.Success("v"));
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        nextCalled.Should().BeFalse();
        cache.ContextGetOrSetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TenantScopedKeyStartingWithTenantMarker_IsEscapedNotRejected()
    {
        var cache = new FakeCacheService();
        var tenant = Guid.NewGuid();
        var behavior = new CachingBehavior<RawKeyQuery, Result<string>>(cache, new FakeRequestContext(tenant));

        await behavior.Handle(new RawKeyQuery("@other:widget"), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.SetCalls.Should().Equal($"@{tenant:D}:%40other%3Awidget");
    }

    [Fact]
    public void ACommandType_CanNeverSatisfyICacheableQuery_ContractShapeOnly()
    {
        // Contract-shape assertion only — CachingBehavior<TRequest,TResponse> is constrained to
        // IQueryBase, which ICommand/ICommand<TResponse> never implement, so no command type can
        // ever be accepted at this generic parameter; there is no reachable runtime case to test.
        typeof(ICommand).Should().NotBeAssignableTo<IQueryBase>();
        typeof(TestQuery).Should().BeAssignableTo<IQueryBase>();
        typeof(TestQuery).Should().BeAssignableTo<ICacheableQuery<string>>();
    }
}
