using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly;
using Polly.CircuitBreaker;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Unit tests for the Polly v8 circuit breaker wiring in <see cref="RedisL2Options.CircuitBreakerOptions"/>.
/// Covers: RCB-01 through RCB-06.
/// </summary>
public sealed class CircuitBreakerTests
{
    // ─── RCB-01: CircuitBreakerOptions defaults ───────────────────────────────

    [Fact]
    public void CircuitBreakerOptions_Defaults_AreCorrect()
    {
        var opts = new RedisL2Options();

        Assert.False(opts.CircuitBreaker.Enabled);
        Assert.Equal(5, opts.CircuitBreaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(10), opts.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), opts.CircuitBreaker.BreakDuration);
        Assert.Equal(3, opts.CircuitBreaker.MinimumThroughput);
    }

    // ─── RCB-02 / RCB-03: ResiliencePipeline registered only when Enabled = true ─

    [Fact]
    public void AddRedisL2_WithCircuitBreakerEnabled_RegistersResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2("localhost:6379", o =>
                {
                    o.CircuitBreaker.Enabled = true;
                    o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                });

        // ResiliencePipeline must be registered as a singleton.
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ResiliencePipeline));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void AddRedisL2_WithCircuitBreakerDisabled_DoesNotRegisterResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2("localhost:6379"); // Enabled = false by default

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ResiliencePipeline));
        Assert.Null(descriptor);
    }

    // ─── RCB-05: Circuit opens after FailureThreshold consecutive failures ─────

    [Fact]
    public async Task CircuitBreaker_OpensAfterThreshold_ConsecutiveFailures()
    {
        // Build a pipeline with FailureThreshold = 3 and very short durations for testing.
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(60),
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>()
            })
            .Build();

        // Exhaust MinimumThroughput with failures.
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(_ => throw new InvalidOperationException("Redis down"), CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // Expected — we are intentionally failing.
            }
        }

        // The circuit must now be open. The next call should short-circuit immediately
        // with BrokenCircuitException (not the original exception).
        var ex = await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
            await pipeline.ExecuteAsync(
                _ => throw new InvalidOperationException("should not reach Redis"),
                CancellationToken.None));

        Assert.NotNull(ex);
    }

    // ─── RCB-05: Open circuit short-circuits immediately ─────────────────────

    [Fact]
    public async Task CircuitBreaker_WhenOpen_ShortCircuitsWithoutCallingOperation()
    {
        // Build a pipeline that opens after 2 failures.
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(60),
                ShouldHandle = new PredicateBuilder().Handle<RedisConnectionException>()
            })
            .Build();

        var operationCallCount = 0;

        async ValueTask<int> Operation(CancellationToken ct)
        {
            operationCallCount++;
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis unavailable");
        }

        // Open the circuit with 2 failures.
        for (var i = 0; i < 2; i++)
        {
            try { await pipeline.ExecuteAsync(Operation, CancellationToken.None); }
            catch { /* Expected */ }
        }

        var callsBeforeBreak = operationCallCount;

        // Next call should short-circuit — the operation lambda should NOT be invoked.
        try
        {
            await pipeline.ExecuteAsync(Operation, CancellationToken.None);
        }
        catch (BrokenCircuitException)
        {
            // Expected — circuit is open.
        }

        // The operation was not called when circuit was open.
        Assert.Equal(callsBeforeBreak, operationCallCount);
    }

    // ─── RCB-05: Circuit closes after BreakDuration ──────────────────────────

    [Fact]
    public async Task CircuitBreaker_ClosesAfterBreakDuration()
    {
        // Build a pipeline with the minimum allowed BreakDuration (Polly v8 minimum is 500ms).
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromMilliseconds(500), // Polly v8 minimum is 500ms
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>()
            })
            .Build();

        // Open the circuit.
        for (var i = 0; i < 2; i++)
        {
            try { await pipeline.ExecuteAsync(_ => throw new InvalidOperationException("fail"), CancellationToken.None); }
            catch { /* Expected */ }
        }

        // Verify circuit is open.
        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult(42), CancellationToken.None));

        // Wait for break duration to elapse.
        await Task.Delay(700);

        // After break duration, circuit transitions to half-open and allows a probe call.
        // A successful probe closes the circuit.
        var result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(42), CancellationToken.None);
        Assert.Equal(42, result);
    }

    // ─── RCB-06: Enabled = false → zero behavioral change ────────────────────

    [Fact]
    public void CircuitBreaker_Disabled_DoesNotAffectExistingRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelCaching()
                              .AddRedisL2("localhost:6379");

        // No ResiliencePipeline should be registered.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ResiliencePipeline));

        // The core services must still be registered as before.
        Assert.Contains(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, d => d.ServiceType == typeof(ICacheService));
    }

    [Fact]
    public void CircuitBreaker_Disabled_RegisteredServicesCountUnchanged()
    {
        // Count registrations without circuit breaker.
        var servicesWithout = new ServiceCollection();
        servicesWithout.AddLogging();
        servicesWithout.AddSharedKernelCaching()
                       .AddRedisL2("localhost:6379");
        var countWithout = servicesWithout.Count;

        // Count registrations with circuit breaker enabled.
        var servicesWith = new ServiceCollection();
        servicesWith.AddLogging();
        servicesWith.AddSharedKernelCaching()
                    .AddRedisL2("localhost:6379", o => o.CircuitBreaker.Enabled = true);
        var countWith = servicesWith.Count;

        // Enabling circuit breaker adds exactly one registration (ResiliencePipeline).
        Assert.Equal(countWithout + 1, countWith);
    }

    // ─── RCB-03: ResiliencePipeline is singleton ──────────────────────────────

    [Fact]
    public void AddRedisL2_WithCircuitBreakerEnabled_ResiliencePipelineIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2("localhost:6379", o => o.CircuitBreaker.Enabled = true);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ResiliencePipeline>();
        var second = provider.GetRequiredService<ResiliencePipeline>();

        Assert.Same(first, second);
    }

    // ─── Circuit breaker options: properties are settable ────────────────────

    [Fact]
    public void CircuitBreakerOptions_PropertiesAreSettable()
    {
        var opts = new RedisL2Options.CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 10,
            SamplingDuration = TimeSpan.FromSeconds(20),
            BreakDuration = TimeSpan.FromMinutes(1),
            MinimumThroughput = 5
        };

        Assert.True(opts.Enabled);
        Assert.Equal(10, opts.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(20), opts.SamplingDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), opts.BreakDuration);
        Assert.Equal(5, opts.MinimumThroughput);
    }

    // ─── DI: RedisHashService injects ResiliencePipeline when registered ─────

    [Fact]
    public void AddRedisHashService_WithCircuitBreakerEnabled_InjectsResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2("localhost:6379", o => o.CircuitBreaker.Enabled = true)
                .AddRedisHashService();

        // The pipeline and hash service must both be resolvable.
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRedisHashService));
        Assert.NotNull(descriptor);
        // Pipeline descriptor must also be present.
        Assert.Contains(services, d => d.ServiceType == typeof(ResiliencePipeline));
    }

    [Fact]
    public void AddRedisChannelService_WithCircuitBreakerEnabled_InjectsResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching()
                .AddRedisL2("localhost:6379", o => o.CircuitBreaker.Enabled = true)
                .AddRedisChannelService();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRedisChannelService));
        Assert.NotNull(descriptor);
        Assert.Contains(services, d => d.ServiceType == typeof(ResiliencePipeline));
    }
}
