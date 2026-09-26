using SharedKernel.Application.Caching;
using SharedKernel.Application.Commands;
using FluentAssertions;
using SharedKernel.Application.Pipeline.Caching;
using SharedKernel.Application.Pipeline.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Caching.Tests.CacheInvalidation;

public sealed class CacheInvalidationBehaviorTests
{
    private sealed record WidgetQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "1";
    }

    private sealed record GlobalWidgetQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "1";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TestCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<WidgetQuery>("1")];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
    }

    private sealed record GlobalCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<GlobalWidgetQuery>("1")];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record KeyOnlyCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<WidgetQuery>("1")];
    }

    private sealed record TagOnlyCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
    }

    private sealed record MultiKeyCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
        [
            CacheKeyRef.For<WidgetQuery>("1"),
            CacheKeyRef.For<WidgetQuery>("2"),
            CacheKeyRef.For<WidgetQuery>("3"),
        ];
    }

    private sealed record DefaultRefCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [default];
    }

    [Fact]
    public async Task Handle_Success_RegistersOnCompletedCallback_DoesNotEvictBeforeItRuns()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(new TenantId(Guid.NewGuid())));

        var result = await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        cache.RemoveCalls.Should().BeEmpty("eviction must be deferred to ICommandScope.OnCompleted, never run directly");
        cache.RemoveByTagCalls.Should().BeEmpty();
        scope.Callbacks.Should().ContainSingle();
    }

    [Fact]
    public async Task OnCompletedCallback_WhenRun_EvictsKeysAndTags()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(tenant));
        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        await scope.RunCallbacksAsync();

        cache.RemoveCalls.Should().Equal(TestKeys.Tenant(tenant, nameof(WidgetQuery), "1"));
        cache.RemoveByTagCalls.Should().Equal(TestKeys.TenantTag(tenant, "widgets"));
    }

    [Fact]
    public async Task Handle_ResultFailure_RegistersNoCallback_AndEvictsNothing()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        await CacheWidgetAsync(cache, tenant, "cached");
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied"))), CancellationToken.None);
        await scope.RunCallbacksAsync();

        scope.Callbacks.Should().BeEmpty();
        cache.RemoveCalls.Should().BeEmpty();
        cache.RemoveByTagCalls.Should().BeEmpty();
        (await ReadWidgetAsync(cache, tenant)).Should().Be("cached");
    }

    [Fact]
    public async Task Handle_ThrownException_RegistersNoCallbackAndRethrows()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(new TenantId(Guid.NewGuid())));

        var act = async () => await behavior.Handle(new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        scope.Callbacks.Should().BeEmpty();
    }

    [Fact]
    public async Task KeyInvalidation_RemovesTheEntryTheCachingBehaviorWrote_OnlyAfterCommit()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        await CacheWidgetAsync(cache, tenant, "stale");
        var behavior = TestPipeline.Invalidation<KeyOnlyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new KeyOnlyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        (await ReadWidgetAsync(cache, tenant)).Should().Be("stale", "nothing is evicted until the command scope completes");

        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenant)).Should().Be("fresh");
    }

    [Fact]
    public async Task TagInvalidation_RemovesTheEntryTheCachingBehaviorWrote()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        await CacheWidgetAsync(cache, tenant, "stale");
        var behavior = TestPipeline.Invalidation<TagOnlyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new TagOnlyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenant)).Should().Be("fresh");
    }

    [Fact]
    public async Task GlobalScopedInvalidation_RemovesTheGlobalEntryTheCachingBehaviorWrote()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var caching = TestPipeline.Caching<GlobalWidgetQuery, Result<string>>(cache);
        await caching.Handle(new GlobalWidgetQuery(), () => Task.FromResult(Result<string>.Success("stale")), CancellationToken.None);

        var behavior = TestPipeline.Invalidation<GlobalCommand, Result>(cache, scope);
        await behavior.Handle(new GlobalCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        var reread = await caching.Handle(
            new GlobalWidgetQuery(), () => Task.FromResult(Result<string>.Success("fresh")), CancellationToken.None);
        reread.Value.Should().Be("fresh");
    }

    [Fact]
    public async Task Invalidation_ForOneTenant_LeavesAnotherTenantsEntry()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenantA = new TenantId(Guid.NewGuid());
        var tenantB = new TenantId(Guid.NewGuid());
        await CacheWidgetAsync(cache, tenantA, "a-stale");
        await CacheWidgetAsync(cache, tenantB, "b-cached");
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(tenantA));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenantA)).Should().Be("fresh");
        (await ReadWidgetAsync(cache, tenantB)).Should().Be("b-cached");
    }

    [Fact]
    public async Task OnCompletedCallback_IsNotCancellableByTheRequestToken()
    {
        // The write has already committed by the time the callback runs, so honouring a cancelled
        // request token would abandon the eviction and leave the cache serving a superseded value.
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        await CacheWidgetAsync(cache, tenant, "stale");
        var behavior = TestPipeline.Invalidation<KeyOnlyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new KeyOnlyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await scope.RunCallbacksAsync(cancelled.Token);

        cache.RemoveCalls.Should().Equal(TestKeys.Tenant(tenant, nameof(WidgetQuery), "1"));
        (await ReadWidgetAsync(cache, tenant)).Should().Be("fresh");
    }

    [Fact]
    public async Task OnCompletedCallback_OneFailingEviction_DoesNotAbandonTheRest()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = new TenantId(Guid.NewGuid());
        var failing = TestKeys.Tenant(tenant, nameof(WidgetQuery), "2");
        cache.FailEvictionsFor(failing);
        var behavior = TestPipeline.Invalidation<MultiKeyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new MultiKeyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        cache.RemoveCalls.Should().Equal(
            TestKeys.Tenant(tenant, nameof(WidgetQuery), "1"),
            failing,
            TestKeys.Tenant(tenant, nameof(WidgetQuery), "3"));
    }

    [Fact]
    public async Task Handle_TenantScopeWithNoResolvedTenant_EvictsNothing()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = TestPipeline.Invalidation<TestCommand, Result>(cache, scope, new FakeRequestContext(tenantId: null));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        scope.Callbacks.Should().BeEmpty("with no scoped target there is nothing to defer");
        cache.RemoveCalls.Should().BeEmpty();
        cache.RemoveByTagCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DefaultCacheKeyRef_ThrowsBeforeTheHandlerRuns()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = TestPipeline.Invalidation<DefaultRefCommand, Result>(cache, scope, new FakeRequestContext(new TenantId(Guid.NewGuid())));
        var handlerRan = false;

        var act = async () => await behavior.Handle(new DefaultRefCommand(), () =>
        {
            handlerRan = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CacheKeyRef.For*");
        handlerRan.Should().BeFalse("an invalid target must fail the command, not the post-commit callback");
    }

    [Fact]
    public void CacheKeyRef_For_RejectsAnEmptyKey()
    {
        var act = () => CacheKeyRef.For<WidgetQuery>("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CacheKeyRef_For_CapturesTheQueryType()
    {
        var reference = CacheKeyRef.For<WidgetQuery>("1");

        reference.QueryType.Should().Be<WidgetQuery>();
        reference.Key.Should().Be("1");
    }

    private static async Task CacheWidgetAsync(FakeCacheService cache, TenantId tenant, string value)
    {
        var caching = TestPipeline.Caching<WidgetQuery, Result<string>>(cache, new FakeRequestContext(tenant));
        await caching.Handle(new WidgetQuery(), () => Task.FromResult(Result<string>.Success(value)), CancellationToken.None);
    }

    // Reads through the caching behaviour; a miss runs the handler, which answers "fresh".
    private static async Task<string> ReadWidgetAsync(FakeCacheService cache, TenantId tenant)
    {
        var caching = TestPipeline.Caching<WidgetQuery, Result<string>>(cache, new FakeRequestContext(tenant));
        var result = await caching.Handle(new WidgetQuery(), () => Task.FromResult(Result<string>.Success("fresh")), CancellationToken.None);
        return result.Value;
    }
}
