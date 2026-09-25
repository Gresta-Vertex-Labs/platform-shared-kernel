using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Proves <see cref="CachingServiceCollectionExtensions.AddFakeCacheWarmupStrategy"/>.</summary>
public sealed class AddFakeCacheWarmupStrategyTests
{
    [Fact]
    public void AddFakeCacheWarmupStrategy_RegistersAsICacheWarmupStrategy()
    {
        var services = new ServiceCollection();
        services.AddFakeCacheWarmupStrategy("strategy-1");
        var provider = services.BuildServiceProvider();

        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.Single(strategies);
        Assert.IsType<FakeCacheWarmupStrategy>(strategies[0]);
        Assert.Equal("strategy-1", strategies[0].Name);
    }

    [Fact]
    public void AddFakeCacheWarmupStrategy_MultipleCallsWithDifferentNames_AllResolveFromEnumerable()
    {
        // Mirrors AddCacheWarmup<TStrategy>()'s own TryAddEnumerable multi-strategy composition
        // shape — several independently-registered named strategies must all resolve together.
        var log = new ConcurrentQueue<string>();
        var services = new ServiceCollection();
        services.AddFakeCacheWarmupStrategy("strategy-a", order: 0, executionLog: log);
        services.AddFakeCacheWarmupStrategy("strategy-b", order: 1, executionLog: log);
        services.AddFakeCacheWarmupStrategy("strategy-c", order: 2, executionLog: log);
        var provider = services.BuildServiceProvider();

        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.Equal(3, strategies.Count);
        Assert.Equal(["strategy-a", "strategy-b", "strategy-c"], strategies.Select(s => s.Name).OrderBy(n => n));
    }

    [Fact]
    public void AddFakeCacheWarmupStrategy_RegisteredStrategyIsASingleton()
    {
        var services = new ServiceCollection();
        services.AddFakeCacheWarmupStrategy("strategy-1");
        var provider = services.BuildServiceProvider();

        var first = provider.GetServices<ICacheWarmupStrategy>().Single();
        var second = provider.GetServices<ICacheWarmupStrategy>().Single();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddFakeCacheWarmupStrategy_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeCacheWarmupStrategy("strategy-1"));
}
