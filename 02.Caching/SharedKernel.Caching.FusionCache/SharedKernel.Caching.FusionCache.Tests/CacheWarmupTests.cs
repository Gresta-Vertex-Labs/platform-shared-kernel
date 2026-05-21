using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="CacheWarmupHostedService"/> and
/// <c>AddCacheWarmup&lt;TStrategy&gt;</c> DI extension.
/// Covers: execution ordering, failure isolation, timing logging, DI idempotence.
/// </summary>
public sealed class CacheWarmupTests
{
    // -------------------------------------------------------------------------
    // Test strategy implementations
    // -------------------------------------------------------------------------

    private sealed class RecordingStrategy(string name, int order, Action? onWarmup = null)
        : ICacheWarmupStrategy
    {
        private int _callCount;

        public string Name { get; } = name;
        public int Order { get; } = order;
        public int CallCount => _callCount;

        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            onWarmup?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OrderCapturingStrategy(
        string name,
        int order,
        List<string> executionLog)
        : ICacheWarmupStrategy
    {
        public string Name { get; } = name;
        public int Order { get; } = order;

        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            executionLog.Add(Name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingStrategy(string name, int order) : ICacheWarmupStrategy
    {
        public string Name { get; } = name;
        public int Order { get; } = order;

        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) =>
            throw new InvalidOperationException($"Strategy '{name}' intentionally failed.");
    }

    private sealed class SlowStrategy(string name, int order, TimeSpan delay)
        : ICacheWarmupStrategy
    {
        public string Name { get; } = name;
        public int Order { get; } = order;

        public async ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) =>
            await Task.Delay(delay, ct);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static (IServiceProvider Provider, ICacheService Cache) BuildProvider(
        Action<IServiceCollection>? extra = null,
        bool waitForWarmup = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => { o.WaitForWarmup = waitForWarmup; });
        extra?.Invoke(services);
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ICacheService>());
    }

    // -------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Strategies_AreExecuted_InAscendingOrderOrder()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        // WaitForWarmup=true so StartedAsync awaits completion before returning.
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);

        // Register in reverse order to confirm sorting takes effect.
        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("StrategyC", order: 30, executionLog));
        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("StrategyA", order: 10, executionLog));
        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("StrategyB", order: 20, executionLog));

        // Register the hosted service manually (not via AddCacheWarmup) to control strategy setup.
        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();

        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StartAsync(cts.Token);
        await hostedService.StartedAsync(cts.Token);

        Assert.Equal(["StrategyA", "StrategyB", "StrategyC"], executionLog);
    }

    [Fact]
    public async Task Strategies_WithSameOrder_AreAllExecuted()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        // WaitForWarmup=true so StartedAsync awaits completion before returning.
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);

        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("X", order: 1, executionLog));
        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("Y", order: 1, executionLog));

        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StartAsync(cts.Token);
        await hostedService.StartedAsync(cts.Token);

        Assert.Equal(2, executionLog.Count);
        Assert.Contains("X", executionLog);
        Assert.Contains("Y", executionLog);
    }

    // -------------------------------------------------------------------------
    // Failure isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FailedStrategy_DoesNotAbortSubsequentStrategies()
    {
        var executionLog = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        // WaitForWarmup=true so StartedAsync awaits completion before returning.
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);

        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("Before", order: 1, executionLog));
        services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy("Failing", order: 2));
        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("After", order: 3, executionLog));

        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Must not throw despite the middle strategy failing.
        await hostedService.StartAsync(cts.Token);
        await hostedService.StartedAsync(cts.Token);

        Assert.Equal(["Before", "After"], executionLog);
    }

    [Fact]
    public async Task AllStrategiesFailing_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // WaitForWarmup=true so StartedAsync awaits completion before returning.
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);

        services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy("FailA", order: 1));
        services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy("FailB", order: 2));

        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Both throw — neither should propagate.
        var exception = await Record.ExceptionAsync(async () =>
        {
            await hostedService.StartAsync(cts.Token);
            await hostedService.StartedAsync(cts.Token);
        });

        Assert.Null(exception);
    }

    // -------------------------------------------------------------------------
    // DI registration — AddCacheWarmup<T>
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWarmup_RegistersStrategyAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching();
        builder.AddCacheWarmup<RecordingStrategy_A>();

        using var provider = services.BuildServiceProvider();
        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.Single(strategies);
        Assert.IsType<RecordingStrategy_A>(strategies[0]);
    }

    [Fact]
    public void AddCacheWarmup_RegistersCacheWarmupHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching();
        builder.AddCacheWarmup<RecordingStrategy_A>();

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(hostedServices, s => s is CacheWarmupHostedService);
    }

    [Fact]
    public void AddCacheWarmup_CalledTwice_RegistersHostedServiceOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching();
        builder.AddCacheWarmup<RecordingStrategy_A>();
        builder.AddCacheWarmup<RecordingStrategy_B>();

        using var provider = services.BuildServiceProvider();

        var warmupHostedServices = provider
            .GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .ToList();

        // Exactly one CacheWarmupHostedService regardless of how many strategies.
        Assert.Single(warmupHostedServices);
    }

    [Fact]
    public void AddCacheWarmup_CalledTwiceWithSameType_RegistersStrategyOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching();
        builder.AddCacheWarmup<RecordingStrategy_A>();
        builder.AddCacheWarmup<RecordingStrategy_A>(); // duplicate

        using var provider = services.BuildServiceProvider();
        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.Single(strategies);
    }

    [Fact]
    public void AddCacheWarmup_CalledWithDifferentTypes_RegistersBothStrategies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching();
        builder.AddCacheWarmup<RecordingStrategy_A>();
        builder.AddCacheWarmup<RecordingStrategy_B>();

        using var provider = services.BuildServiceProvider();
        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.Equal(2, strategies.Count);
    }

    // -------------------------------------------------------------------------
    // WaitForWarmup = true — StartedAsync awaits warmup before returning
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WaitForWarmup_True_StartedAsync_AwaitsWarmupCompletion()
    {
        var completedNames = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);

        services.AddSingleton<ICacheWarmupStrategy>(
            new OrderCapturingStrategy("WarmOne", order: 1, completedNames));

        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await hostedService.StartAsync(cts.Token);
        await hostedService.StartedAsync(cts.Token);

        // After StartedAsync returns, warmup must be done.
        Assert.Contains("WarmOne", completedNames);
    }

    [Fact]
    public async Task WaitForWarmup_False_StartedAsync_ReturnsImmediately()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.WaitForWarmup = false);

        // Use a slow strategy — StartedAsync should return before it finishes when WaitForWarmup=false.
        services.AddSingleton<ICacheWarmupStrategy>(
            new SlowStrategy("SlowOne", order: 1, delay: TimeSpan.FromMinutes(10)));

        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await hostedService.StartAsync(cts.Token);

        // Should complete immediately without waiting for the slow warmup.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await hostedService.StartedAsync(cts.Token);
        elapsed.Stop();

        Assert.True(elapsed.ElapsedMilliseconds < 500,
            $"StartedAsync should return immediately when WaitForWarmup=false, but took {elapsed.ElapsedMilliseconds}ms");

        // Stop the service to cancel the background warmup.
        await hostedService.StopAsync(CancellationToken.None);
    }

    // -------------------------------------------------------------------------
    // No strategies registered
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NoStrategiesRegistered_CompletesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.WaitForWarmup = true);
        services.AddSingleton<IHostedService, CacheWarmupHostedService>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<CacheWarmupHostedService>()
            .Single();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var exception = await Record.ExceptionAsync(async () =>
        {
            await hostedService.StartAsync(cts.Token);
            await hostedService.StartedAsync(cts.Token);
        });

        Assert.Null(exception);
    }

    // -------------------------------------------------------------------------
    // Helper strategy types for DI registration tests (must be concrete types)
    // -------------------------------------------------------------------------

    private sealed class RecordingStrategy_A : ICacheWarmupStrategy
    {
        public string Name => "RecordingA";
        public int Order => 1;
        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class RecordingStrategy_B : ICacheWarmupStrategy
    {
        public string Name => "RecordingB";
        public int Order => 2;
        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
