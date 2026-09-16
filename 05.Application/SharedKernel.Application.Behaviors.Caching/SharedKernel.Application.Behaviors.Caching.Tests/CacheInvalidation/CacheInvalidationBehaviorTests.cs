using FluentAssertions;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Messaging;
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

        cache.RemoveCalls.Should().Contain("widget:1");
        cache.RemoveByTagCalls.Should().Contain("widgets");
    }

    [Fact]
    public async Task Handle_ResultFailure_RegistersNoCallback()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope);

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Failure(Error.BusinessRule("rule", "denied"))), CancellationToken.None);

        scope.Callbacks.Should().BeEmpty();
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
    public async Task Handle_TenantScoped_EvictsTenantPrefixedKeysAndTags()
    {
        var cache = new FakeCacheService();
        var scope = new FakeCommandScope();
        var tenant = Guid.NewGuid();
        var behavior = new CacheInvalidationBehavior<TestCommand, Result>(cache, scope, new FakeRequestContext(tenant));

        await behavior.Handle(new TestCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        cache.RemoveCalls.Should().Contain($"tenant:{tenant}:widget:1");
        cache.RemoveByTagCalls.Should().Contain($"tenant:{tenant}:widgets");
    }
}
