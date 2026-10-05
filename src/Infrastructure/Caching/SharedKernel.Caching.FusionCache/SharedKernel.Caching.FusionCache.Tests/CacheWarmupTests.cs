using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Tests.Telemetry;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Tests for <c>AddCacheWarmup&lt;TStrategy&gt;</c> and the internal warmup lifecycle service, driven
/// through a real <see cref="IHost"/> so the host's own <c>StartingAsync</c>/<c>StartAsync</c>/<c>StopAsync</c>
/// ordering is what is under test.
/// </summary>
public sealed class CacheWarmupTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>The ordered record of what happened, shared by strategies and the probe service.</summary>
    private sealed class EventLog
    {
        private readonly ConcurrentQueue<string> _events = new();

        public void Add(string entry) => _events.Enqueue(entry);

        public IReadOnlyList<string> Entries => _events.ToArray();
    }

    private sealed class RecordingStrategy(string name, int order, EventLog log) : ICacheWarmupStrategy
    {
        public string Name => name;
        public int Order => order;

        public async ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            await cache.SetAsync($"svc:warmup:{name}", name, CachePolicy.Default, ct);
            log.Add(name);
        }
    }

    private sealed class ThrowingStrategy(string name, int order, EventLog log) : ICacheWarmupStrategy
    {
        public string Name => name;
        public int Order => order;

        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            log.Add(name + ":threw");
            throw new InvalidOperationException($"Strategy '{name}' intentionally failed.");
        }
    }

    /// <summary>Takes a noticeable time, then records completion.</summary>
    private sealed class SlowStrategy(EventLog log, TimeSpan delay) : ICacheWarmupStrategy
    {
        public string Name => "slow";
        public int Order => 0;

        public async ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            log.Add("warmup-started");
            await Task.Delay(delay, ct);
            log.Add("warmup-completed");
        }
    }

    /// <summary>Runs until cancelled, reporting that it started and how it ended.</summary>
    private sealed class BlockingStrategy : ICacheWarmupStrategy
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }

        public string Name => "blocking";
        public int Order => 0;

        public async ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                Completed = true;
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    /// <summary>A hosted service registered after the warmup; records when the host starts it.</summary>
    private sealed class ProbeHostedService(EventLog log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Add("probe-started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StrategyA : ICacheWarmupStrategy
    {
        public string Name => "A";
        public int Order => 1;
        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class StrategyB : ICacheWarmupStrategy
    {
        public string Name => "B";
        public int Order => 2;
        public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct) => ValueTask.CompletedTask;
    }

    private static IHost BuildHost(bool waitForWarmup, Action<IServiceCollection, ICachingBuilder> configure, ILoggerProvider? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
                b.AddProvider(logs);
        });

        var caching = builder.Services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "svc";
            o.WaitForWarmup = waitForWarmup;
        });
        configure(builder.Services, caching);

        return builder.Build();
    }

    // -------------------------------------------------------------------------
    // WaitForWarmup = true
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WaitForWarmup_True_WarmupCompletesBeforeHostStartReturns_AndBeforeOtherHostedServicesStart()
    {
        var log = new EventLog();
        using var host = BuildHost(waitForWarmup: true, (services, caching) =>
        {
            services.AddSingleton(log);
            services.AddSingleton<ICacheWarmupStrategy>(new SlowStrategy(log, TimeSpan.FromMilliseconds(300)));
            caching.AddCacheWarmup<StrategyA>();
            // Registered after the warmup, like a web server or a consumer would be.
            services.AddHostedService<ProbeHostedService>();
        });

        await host.StartAsync().WaitAsync(Timeout);

        Assert.Equal(["warmup-started", "warmup-completed", "probe-started"], log.Entries);

        await host.StopAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitForWarmup_True_StrategiesRunInAscendingOrder_AndFillTheCache()
    {
        var log = new EventLog();
        using var host = BuildHost(waitForWarmup: true, (services, caching) =>
        {
            // Registered out of order to prove sorting.
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("C", 30, log));
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("A", 10, log));
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("B", 20, log));
            caching.AddCacheWarmup<StrategyA>();
        });

        await host.StartAsync().WaitAsync(Timeout);

        Assert.Equal(["A", "B", "C"], log.Entries);
        var cache = host.Services.GetRequiredService<ICacheService>();
        Assert.Equal("B", (await cache.TryGetAsync<string>("svc:warmup:B")).Value);

        await host.StopAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitForWarmup_True_ThrowingStrategy_IsLoggedAndSkipped_OthersStillRun()
    {
        var log = new EventLog();
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(waitForWarmup: true, (services, caching) =>
        {
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("before", 1, log));
            services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy("failing", 2, log));
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("after", 3, log));
            caching.AddCacheWarmup<StrategyA>();
        }, logs);

        await host.StartAsync().WaitAsync(Timeout);

        Assert.Equal(["before", "failing:threw", "after"], log.Entries);
        var failure = Assert.Single(logs.Logs, l => l.Level == LogLevel.Error && l.Message.Contains("failing", StringComparison.Ordinal));
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Contains(logs.Logs, l => l.Message == "Cache warmup completed");

        await host.StopAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitForWarmup_True_CancelledStartup_PropagatesCancellation_AndDoesNotStartOtherServices()
    {
        var log = new EventLog();
        var blocking = new BlockingStrategy();
        using var host = BuildHost(waitForWarmup: true, (services, caching) =>
        {
            services.AddSingleton(log);
            services.AddSingleton<ICacheWarmupStrategy>(blocking);
            caching.AddCacheWarmup<StrategyA>();
            services.AddHostedService<ProbeHostedService>();
        });

        using var cts = new CancellationTokenSource();
        var start = host.StartAsync(cts.Token);
        await blocking.Started.Task.WaitAsync(Timeout);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(Timeout));
        await blocking.Cancelled.Task.WaitAsync(Timeout);
        Assert.False(blocking.Completed);
        Assert.DoesNotContain("probe-started", log.Entries);
    }

    [Fact]
    public async Task NoStrategiesRegistered_HostStartsNormally()
    {
        var log = new EventLog();
        using var host = BuildHost(waitForWarmup: true, (services, _) =>
        {
            services.AddSingleton(log);
            // The warmup service with no strategy registered.
            services.AddSingleton<IHostedService, CacheWarmupHostedService>();
            services.AddHostedService<ProbeHostedService>();
        });

        await host.StartAsync().WaitAsync(Timeout);

        Assert.Equal(["probe-started"], log.Entries);

        await host.StopAsync().WaitAsync(Timeout);
    }

    // -------------------------------------------------------------------------
    // WaitForWarmup = false
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WaitForWarmup_False_HostStartReturnsBeforeWarmupFinishes_AndStopCancelsIt()
    {
        var log = new EventLog();
        var blocking = new BlockingStrategy();
        using var host = BuildHost(waitForWarmup: false, (services, caching) =>
        {
            services.AddSingleton(log);
            services.AddSingleton<ICacheWarmupStrategy>(blocking);
            caching.AddCacheWarmup<StrategyA>();
            services.AddHostedService<ProbeHostedService>();
        });

        await host.StartAsync().WaitAsync(Timeout);

        // Start returned while the strategy is still running, and the other hosted service started.
        await blocking.Started.Task.WaitAsync(Timeout);
        Assert.False(blocking.Completed);
        Assert.Contains("probe-started", log.Entries);

        await host.StopAsync().WaitAsync(Timeout);

        // StopAsync cancelled the background warmup and awaited it.
        Assert.True(blocking.Cancelled.Task.IsCompleted, "StopAsync must cancel the running warmup before it returns.");
        Assert.False(blocking.Completed);
    }

    [Fact]
    public async Task WaitForWarmup_False_WarmupStillRunsInTheBackground_WithFailuresSkipped()
    {
        var log = new EventLog();
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(waitForWarmup: false, (services, caching) =>
        {
            services.AddSingleton<ICacheWarmupStrategy>(new ThrowingStrategy("failing", 1, log));
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("after", 2, log));
            caching.AddCacheWarmup<StrategyA>();
        }, logs);

        await host.StartAsync().WaitAsync(Timeout);

        Assert.True(await Eventually.HoldsAsync(() => logs.Logs.Any(l => l.Message == "Cache warmup completed")));
        Assert.Equal(["failing:threw", "after"], log.Entries);

        await host.StopAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task WaitForWarmup_False_StopAfterWarmupFinished_Completes()
    {
        var log = new EventLog();
        using var host = BuildHost(waitForWarmup: false, (services, caching) =>
        {
            services.AddSingleton<ICacheWarmupStrategy>(new RecordingStrategy("quick", 1, log));
            caching.AddCacheWarmup<StrategyA>();
        });

        await host.StartAsync().WaitAsync(Timeout);
        Assert.True(await Eventually.HoldsAsync(() => log.Entries.Contains("quick")));

        await host.StopAsync().WaitAsync(Timeout);
    }

    // -------------------------------------------------------------------------
    // DI registration — AddCacheWarmup<T>
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWarmup_RegistersStrategyAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc").AddCacheWarmup<StrategyA>();

        using var provider = services.BuildServiceProvider();
        var strategies = provider.GetServices<ICacheWarmupStrategy>().ToList();

        Assert.IsType<StrategyA>(Assert.Single(strategies));
        Assert.Same(strategies[0], provider.GetServices<ICacheWarmupStrategy>().Single());
    }

    [Fact]
    public void AddCacheWarmup_RegistersOneLifecycleHostedService_RegardlessOfStrategyCount()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
            .AddCacheWarmup<StrategyA>()
            .AddCacheWarmup<StrategyB>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Equal(typeof(CacheWarmupHostedService), descriptor.ImplementationType);
        Assert.True(typeof(IHostedLifecycleService).IsAssignableFrom(descriptor.ImplementationType));
        Assert.False(descriptor.ImplementationType!.IsPublic);
    }

    [Fact]
    public void AddCacheWarmup_CalledTwiceWithSameType_RegistersStrategyOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
            .AddCacheWarmup<StrategyA>()
            .AddCacheWarmup<StrategyA>();

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<ICacheWarmupStrategy>());
    }

    [Fact]
    public void AddCacheWarmup_CalledWithDifferentTypes_RegistersBothStrategies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
            .AddCacheWarmup<StrategyA>()
            .AddCacheWarmup<StrategyB>();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, provider.GetServices<ICacheWarmupStrategy>().Count());
    }

    [Fact]
    public void AddCacheWarmup_NullBuilder_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((ICachingBuilder)null!).AddCacheWarmup<StrategyA>());
}
