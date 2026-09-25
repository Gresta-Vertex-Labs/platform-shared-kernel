using FluentAssertions;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Caching;

public sealed class CachingBehaviorTests
{
    private sealed record GlobalQuery(string Id) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => Id;
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TaggedGlobalQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "tagged";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TaggedTenantQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "tagged";
    }

    private sealed record TenantQuery(string Id) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => Id;
    }

    private sealed record ConfiguredPolicyQuery(CachePolicy CachePolicy) : ICacheableQuery<string>
    {
        public string CacheKey => "configured";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record MismatchedQuery : ICacheableQuery<string>, MediatR.IRequest<int>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "mismatched";
        public CacheScope Scope => CacheScope.Global;
    }

    [Fact]
    public async Task Handle_CacheMiss_InvokesNextOnceAndCachesSuccess()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<GlobalQuery, Result<string>>(cache);
        var nextCallCount = 0;

        var result = await behavior.Handle(new GlobalQuery("1"), () =>
        {
            nextCallCount++;
            return Task.FromResult(Result<string>.Success("widget-1"));
        }, CancellationToken.None);

        result.Value.Should().Be("widget-1");
        nextCallCount.Should().Be(1);
        cache.SetCalls.Should().ContainSingle().Which.Should().Be(TestKeys.Global(nameof(GlobalQuery), "1"));
    }

    [Fact]
    public async Task Handle_CacheHit_ServesCachedValueWithoutInvokingNext()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<GlobalQuery, Result<string>>(cache);

        await behavior.Handle(new GlobalQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        var nextCalled = false;
        var second = await behavior.Handle(new GlobalQuery("1"), () =>
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
        var behavior = TestPipeline.Caching<GlobalQuery, Result<string>>(cache);
        var key = TestKeys.Global(nameof(GlobalQuery), "1");

        await behavior.Handle(new GlobalQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        (await cache.TryGetAsync<string>(key)).TryGetValue(out var cached).Should().BeTrue();
        cached.Should().Be("widget-1");
        (await cache.TryGetAsync<Result<string>>(key)).IsHit.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ResponseIsNotTheQueryResult_ThrowsWithoutInvokingNext()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<MismatchedQuery, int>(cache);
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
        var behavior = TestPipeline.Caching<GlobalQuery, Result<string>>(cache);

        var result = await behavior.Handle(
            new GlobalQuery("1"),
            () => Task.FromResult(Result<string>.Failure(Error.NotFound("test.not_found", "missing"))),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("test.not_found");
        cache.SetCalls.Should().BeEmpty();
        cache.Keys.Should().BeEmpty();

        var nextCalled = false;
        var second = await behavior.Handle(new GlobalQuery("1"), () =>
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
        var behavior = TestPipeline.Caching<GlobalQuery, Result<string>>(cache);

        await behavior.Handle(new GlobalQuery("1"), () => Task.FromResult(Result<string>.Success("widget-1")), CancellationToken.None);

        cache.ContextGetOrSetCalls.Should().ContainSingle()
            .Which.Key.Should().Be(TestKeys.Global(nameof(GlobalQuery), "1"));
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
        var behavior = TestPipeline.Caching<ConfiguredPolicyQuery, Result<string>>(cache);

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
    public async Task Handle_GlobalScope_UsesUnscopedKeyAndTagsEvenWithATenantPresent()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<TaggedGlobalQuery, Result<string>>(cache, new FakeRequestContext(new TenantId(Guid.NewGuid())));

        await behavior.Handle(new TaggedGlobalQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        var key = TestKeys.Global(nameof(TaggedGlobalQuery), "tagged");
        cache.SetCalls.Should().Equal(key);
        cache.GetTags(key).Should().Equal("widgets");
        cache.ContextGetOrSetCalls.Single().Policy.IsTenantScoped.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TenantScoped_KeyAndPolicyUseTenantFormat()
    {
        var cache = new FakeCacheService();
        var tenant = new TenantId(Guid.NewGuid());
        var tenantId = tenant;
        var behavior = TestPipeline.Caching<TaggedTenantQuery, Result<string>>(cache, new FakeRequestContext(tenant));

        await behavior.Handle(new TaggedTenantQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        var (key, policy) = cache.ContextGetOrSetCalls.Should().ContainSingle().Subject;
        key.Should().Be(TestKeys.Tenant(tenant, nameof(TaggedTenantQuery), "tagged"));
        key.Should().Be($"{FakeCacheKeyProvider.ServiceName}:@{tenantId}:{nameof(TaggedTenantQuery)}:tagged");
        policy.IsTenantScoped.Should().BeTrue();
        policy.Tags.Should().Equal($"@{tenantId}:widgets", $"@{tenantId}");
        cache.GetTags(key).Should().Equal($"@{tenantId}:widgets", $"@{tenantId}");
    }

    [Fact]
    public async Task Handle_DifferentTenants_ProduceDifferentCacheKeys_NeitherReadsTheOthers()
    {
        var cache = new FakeCacheService();
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        var behaviorA = TestPipeline.Caching<TenantQuery, Result<string>>(cache, new FakeRequestContext(tenantA));
        var behaviorB = TestPipeline.Caching<TenantQuery, Result<string>>(cache, new FakeRequestContext(tenantB));

        await behaviorA.Handle(new TenantQuery("1"), () => Task.FromResult(Result<string>.Success("a-value")), CancellationToken.None);

        var bCalledNext = false;
        var bResult = await behaviorB.Handle(new TenantQuery("1"), () =>
        {
            bCalledNext = true;
            return Task.FromResult(Result<string>.Success("b-value"));
        }, CancellationToken.None);

        bCalledNext.Should().BeTrue("tenant B must not read the tenant A cache entry for the same logical key");
        bResult.Value.Should().Be("b-value");
        cache.SetCalls.Should().Equal(
            TestKeys.Tenant(tenantA, nameof(TenantQuery), "1"),
            TestKeys.Tenant(tenantB, nameof(TenantQuery), "1"));

        var aAgain = await behaviorA.Handle(new TenantQuery("1"), () => Task.FromResult(Result<string>.Success("unexpected")), CancellationToken.None);
        aAgain.Value.Should().Be("a-value");
    }

    [Fact]
    public async Task Handle_TenantScopedTagEviction_RemovesOnlyThatTenantsEntry()
    {
        var cache = new FakeCacheService();
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        var behaviorA = TestPipeline.Caching<TaggedTenantQuery, Result<string>>(cache, new FakeRequestContext(tenantA));
        var behaviorB = TestPipeline.Caching<TaggedTenantQuery, Result<string>>(cache, new FakeRequestContext(tenantB));

        await behaviorA.Handle(new TaggedTenantQuery(), () => Task.FromResult(Result<string>.Success("a")), CancellationToken.None);
        await behaviorB.Handle(new TaggedTenantQuery(), () => Task.FromResult(Result<string>.Success("b")), CancellationToken.None);

        await cache.RemoveByTagAsync(TestKeys.TenantTag(tenantA, "widgets"));

        (await cache.TryGetAsync<string>(TestKeys.Tenant(tenantA, nameof(TaggedTenantQuery), "tagged"))).IsHit.Should().BeFalse();
        (await cache.TryGetAsync<string>(TestKeys.Tenant(tenantB, nameof(TaggedTenantQuery), "tagged"))).IsHit.Should().BeTrue();
    }

    [Fact]
    public void ACommandType_CanNeverSatisfyICacheableQuery_ContractShapeOnly()
    {
        // Contract-shape assertion only: the caching behavior is constrained to IQueryBase, which
        // ICommand/ICommand<TResponse> never implement, so no command type can ever be accepted at
        // that generic parameter; there is no reachable runtime case to test.
        typeof(ICommand).Should().NotBeAssignableTo<IQueryBase>();
        typeof(GlobalQuery).Should().BeAssignableTo<IQueryBase>();
        typeof(GlobalQuery).Should().BeAssignableTo<ICacheableQuery<string>>();
    }
}
