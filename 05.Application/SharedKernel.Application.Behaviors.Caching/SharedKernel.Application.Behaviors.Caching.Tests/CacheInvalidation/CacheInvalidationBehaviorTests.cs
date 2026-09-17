using FluentAssertions;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.CacheInvalidation;

public sealed class CacheInvalidationBehaviorTests
{
    private sealed record TestCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["widget:1"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
    }

    private sealed record KeyOnlyCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => ["widget:1"];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => [];
    }

    private sealed record TagOnlyCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => [];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
    }

    private sealed record WidgetQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default.WithTags("widgets");
        public string CacheKey => "widget:1";
    }

    [Fact]
    public async Task Handle_Success_RegistersOnCompletedCallback_DoesNotEvictBeforeItRuns()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope);

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
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope);
        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        await scope.RunCallbacksAsync();

        cache.RemoveCalls.Should().Equal("widget:1");
        cache.RemoveByTagCalls.Should().Equal("widgets");
    }

    [Fact]
    public async Task Handle_ResultFailure_RegistersNoCallback_AndEvictsNothing()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = Guid.NewGuid();
        await CacheWidgetAsync(cache, tenant, "cached");
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope, new FakeRequestContext(tenant));

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
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope);

        var act = async () => await behavior.Handle(new TestCommand(), () => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        scope.Callbacks.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TenantScoped_EvictsTenantScopedKeysAndTags()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = Guid.NewGuid();
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        cache.RemoveCalls.Should().Equal(CacheKeyFormat.BuildTenantTag(tenant.ToString("D"), "widget:1"));
        cache.RemoveByTagCalls.Should().Equal(CacheKeyFormat.BuildTenantTag(tenant.ToString("D"), "widgets"));
    }

    [Fact]
    public async Task KeyInvalidation_RemovesTheEntryCachingBehaviorWrote_OnlyAfterCommit()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = Guid.NewGuid();
        await CacheWidgetAsync(cache, tenant, "stale");
        var behavior = new CacheInvalidationBehavior<KeyOnlyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new KeyOnlyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        (await ReadWidgetAsync(cache, tenant)).Should().Be("stale", "nothing is evicted until the command scope completes");

        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenant)).Should().Be("fresh");
    }

    [Fact]
    public async Task TagInvalidation_RemovesTheEntryCachingBehaviorWrote()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = Guid.NewGuid();
        await CacheWidgetAsync(cache, tenant, "stale");
        var behavior = new CacheInvalidationBehavior<TagOnlyCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new TagOnlyCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenant)).Should().Be("fresh");
    }

    [Fact]
    public async Task Invalidation_WithoutTenant_RemovesTheGlobalEntryCachingBehaviorWrote()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        await CacheWidgetAsync(cache, tenant: null, "stale");
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope);

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenant: null)).Should().Be("fresh");
    }

    [Fact]
    public async Task Invalidation_ForOneTenant_LeavesOtherTenantsEntry()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await CacheWidgetAsync(cache, tenantA, "a-stale");
        await CacheWidgetAsync(cache, tenantB, "b-cached");
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope, new FakeRequestContext(tenantA));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        (await ReadWidgetAsync(cache, tenantA)).Should().Be("fresh");
        (await ReadWidgetAsync(cache, tenantB)).Should().Be("b-cached");
    }

    private static async Task CacheWidgetAsync(FakeCacheService cache, Guid? tenant, string value)
    {
        var caching = new CachingBehavior<WidgetQuery, Result<string>>(cache, new FakeRequestContext(tenant));
        await caching.Handle(new WidgetQuery(), () => Task.FromResult(Result<string>.Success(value)), CancellationToken.None);
    }

    // Reads through CachingBehavior; a miss runs the handler, which answers "fresh".
    private static async Task<string> ReadWidgetAsync(FakeCacheService cache, Guid? tenant)
    {
        var caching = new CachingBehavior<WidgetQuery, Result<string>>(cache, new FakeRequestContext(tenant));
        var result = await caching.Handle(new WidgetQuery(), () => Task.FromResult(Result<string>.Success("fresh")), CancellationToken.None);
        return result.Value;
    }
}
