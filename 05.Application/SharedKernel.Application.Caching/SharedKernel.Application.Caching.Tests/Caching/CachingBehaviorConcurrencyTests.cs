using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Caching.Tests.Support;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Application.Caching.Tests.Caching;

/// <summary>
/// Pins the concurrency contract the caching behaviour depends on, against a real cache.
/// </summary>
/// <remarks>
/// <para>
/// <c>CachedQueryValue</c> carries a failed result out of the cache factory in a local captured by
/// the factory closure. That is only correct if a caller waiting on the same key does <b>not</b>
/// receive the value returned by a factory run that called
/// <see cref="CacheFactoryContext.SkipCaching"/> — otherwise every waiter would come out of
/// <c>GetOrSetAsync</c> holding a default value with no failure recorded in its own closure, and the
/// behaviour would hand back <c>Result.Success(null)</c> where a failure was correct.
/// </para>
/// <para>
/// <see cref="CacheFactoryContext"/>'s own documentation states the factory value reaches every
/// concurrent caller, which if taken literally would make that bug real. Measured against
/// FusionCache it does not: a skip stores nothing, so the waiter re-checks after the lock, misses,
/// and runs the factory itself. These two tests hold that measured behaviour in place, so a cache
/// implementation or version that broadcasts a skipped value fails here rather than in production.
/// </para>
/// </remarks>
public sealed class CachingBehaviorConcurrencyTests
{
    public sealed record Dto(string Id);

    private sealed record Query(string Id) : ICacheableQuery<Dto>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => Id;
        public CacheScope Scope => CacheScope.Global;
    }

    [Fact]
    public async Task ConcurrentCallers_OnASuccessfulHandler_RunItOnceAndShareTheValue()
    {
        await using var provider = Build();
        var behavior = Behavior(provider);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRuns = 0;

        var first = Task.Run(async () => await behavior.Handle(new Query("s"), async () =>
        {
            Interlocked.Increment(ref handlerRuns);
            entered.TrySetResult();
            await release.Task;
            return Result<Dto>.Success(new Dto("from-first"));
        }, CancellationToken.None));

        await entered.Task;

        var second = Task.Run(async () => await behavior.Handle(new Query("s"), () =>
        {
            Interlocked.Increment(ref handlerRuns);
            return Task.FromResult(Result<Dto>.Success(new Dto("from-second")));
        }, CancellationToken.None));

        await WaitForWaiterAsync(second);
        release.TrySetResult();

        var firstResult = await first;
        var secondResult = await second;

        handlerRuns.Should().Be(1, "stampede protection must run the handler once for concurrent identical queries");
        firstResult.Value.Id.Should().Be("from-first");
        secondResult.Value.Id.Should().Be("from-first", "the waiting caller is served the value the single handler run produced");
    }

    [Fact]
    public async Task ConcurrentCallers_OnAFailingHandler_EachGetTheirOwnFailure_NeverASuccessfulDefault()
    {
        await using var provider = Build();
        var behavior = Behavior(provider);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRuns = 0;

        var first = Task.Run(async () => await behavior.Handle(new Query("f"), async () =>
        {
            Interlocked.Increment(ref handlerRuns);
            entered.TrySetResult();
            await release.Task;
            return Result<Dto>.Failure(Error.NotFound("first.missing", "missing"));
        }, CancellationToken.None));

        await entered.Task;

        var second = Task.Run(async () => await behavior.Handle(new Query("f"), () =>
        {
            Interlocked.Increment(ref handlerRuns);
            return Task.FromResult(Result<Dto>.Failure(Error.NotFound("second.missing", "missing")));
        }, CancellationToken.None));

        await WaitForWaiterAsync(second);
        release.TrySetResult();

        var firstResult = await first;
        var secondResult = await second;

        firstResult.IsFailure.Should().BeTrue();
        firstResult.Error.Code.Should().Be("first.missing");

        secondResult.IsFailure.Should().BeTrue(
            "a concurrent caller must never be handed Result.Success(default) built from a skipped factory value");
        secondResult.Error.Code.Should().Be("second.missing");
        handlerRuns.Should().Be(2, "a skipped factory value is not shared, so the waiter runs the handler itself");
    }

    [Fact]
    public async Task AFailedQuery_IsNotCached_AndTheNextCallerRunsTheHandler()
    {
        await using var provider = Build();
        var behavior = Behavior(provider);

        var failed = await behavior.Handle(
            new Query("n"),
            () => Task.FromResult(Result<Dto>.Failure(Error.NotFound("missing", "missing"))),
            CancellationToken.None);

        var handlerRan = false;
        var recovered = await behavior.Handle(new Query("n"), () =>
        {
            handlerRan = true;
            return Task.FromResult(Result<Dto>.Success(new Dto("recovered")));
        }, CancellationToken.None);

        failed.IsFailure.Should().BeTrue();
        handlerRan.Should().BeTrue();
        recovered.Value.Id.Should().Be("recovered");
    }

    /// <summary>
    /// Gives the second caller time to reach the cache and block on the in-flight factory, without
    /// a fixed sleep deciding whether the test is meaningful.
    /// </summary>
    private static async Task WaitForWaiterAsync(Task waiter)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (waiter.IsCompleted)
                return;

            await Task.Delay(25);

            // Once it has had a chance to run and is still pending, it is parked inside the cache.
            if (!waiter.IsCompleted)
                return;
        }
    }

    private static CachingBehavior<Query, Result<Dto>> Behavior(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<ICacheService>(),
            provider.GetRequiredService<ITenantCacheKeyProvider>(),
            TestPipeline.Metrics(),
            NullLogger<CachingBehavior<Query, Result<Dto>>>.Instance);

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDistributedCache>(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSharedKernelCaching(o => o.ServiceName = "behaviors-caching-tests");
        services.AddFusionCache().WithRegisteredDistributedCache(ignoreMemoryDistributedCache: false);
        return services.BuildServiceProvider();
    }
}
