using System.Collections.Concurrent;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;

namespace SharedKernel.Application.Behaviors.Tests.Caching;

/// <summary>
/// Verifies <see cref="CachingBehavior{TRequest,TResponse}"/> miss/hit/stampede semantics against
/// a real <see cref="FakeCacheService"/> test double, and that a command type can never satisfy
/// <see cref="ICacheableQuery{TResponse}"/>.
/// </summary>
public sealed class CachingBehaviorTests
{
    private sealed record TestQuery(string CacheKey) : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
    }

    private sealed class CountingHandler : IRequestHandler<TestQuery, string>
    {
        private int _invocationCount;
        public int InvocationCount => _invocationCount;

        public async Task<string> Handle(TestQuery request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            await Task.Delay(50, cancellationToken);
            return $"value-for-{request.CacheKey}";
        }
    }

    private static ServiceProvider BuildProvider(FakeCacheService cacheService, CountingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(cacheService);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestQuery, string>>(sp => sp.GetRequiredService<CountingHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CachingBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_OnCacheMiss_InvokesFactoryOnceAndCachesResult()
    {
        var cacheService = new FakeCacheService();
        var handler = new CountingHandler();
        var provider = BuildProvider(cacheService, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestQuery("key-1"));

        result.Should().Be("value-for-key-1");
        handler.InvocationCount.Should().Be(1);
        cacheService.Count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OnCacheHit_DoesNotInvokeFactory()
    {
        var cacheService = new FakeCacheService();
        var handler = new CountingHandler();
        var provider = BuildProvider(cacheService, handler);
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestQuery("key-2"));
        handler.InvocationCount.Should().Be(1);

        var second = await sender.Send(new TestQuery("key-2"));

        second.Should().Be("value-for-key-2");
        handler.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// A minimal stampede-protected <see cref="ICacheService"/> test double — per-key
    /// <see cref="SemaphoreSlim"/> locking around the factory call. <see cref="FakeCacheService"/>
    /// (<c>16.Testing</c>) is documented as verifying behavioral correctness only, not concurrency
    /// semantics, so it cannot itself prove stampede protection; this double exists solely to
    /// exercise <see cref="CachingBehavior{TRequest,TResponse}"/>'s call shape
    /// (<c>GetOrSetAsync</c>, never <c>GetAsync</c>+<c>SetAsync</c>) under genuine concurrent load.
    /// </summary>
    private sealed class StampedeProtectedCacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, object?> _store = new();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => ValueTask.FromResult(_store.TryGetValue(key, out var v) && v is T typed ? typed : default);

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
        {
            _store[key] = value;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing) && existing is T typedExisting)
                return typedExisting;

            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_store.TryGetValue(key, out var raced) && raced is T typedRaced)
                    return typedRaced;

                var value = await factory(ct).ConfigureAwait(false);
                _store[key] = value;
                return value;
            }
            finally
            {
                gate.Release();
            }
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyDictionary<string, T?>>(
                keys.ToDictionary(k => k, k => _store.TryGetValue(k, out var v) && v is T typed ? typed : default));

        public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        {
            foreach (var (k, v) in entries)
                _store[k] = v;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Handle_ConcurrentRequestsForSameKey_InvokesFactoryExactlyOnce()
    {
        var cacheService = new StampedeProtectedCacheService();
        var handler = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(cacheService);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestQuery, string>>(sp => sp.GetRequiredService<CountingHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CachingBehaviorTests>());
        var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sender.Send(new TestQuery("stampede-key")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "value-for-stampede-key");
        handler.InvocationCount.Should().Be(1);
    }

    private sealed record NotAQuery : ICommand;

    [Fact]
    public void ICacheableQuery_IsNeverSatisfiedByACommandType()
    {
        // Contract-shape assertion only — no runtime case is reachable because no command type in
        // this domain implements ICacheableQuery<TResponse>.
        typeof(NotAQuery).Should().NotBeAssignableTo<ICacheableQuery<object>>();
        typeof(NotAQuery).Should().BeAssignableTo<ICommandBase>();
    }
}
