using Microsoft.Extensions.DependencyInjection;
using Polly;
using SharedKernel.Caching.Redis.Core.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RedisCircuitBreakerOptions"/> and
/// <see cref="RedisCircuitBreakerExtensions.AddRedisCircuitBreaker"/>.
/// Covers RC-03 and RC-06.
/// </summary>
public sealed class RedisCircuitBreakerTests
{
    [Fact]
    public void RedisCircuitBreakerOptions_Defaults_AreCorrect()
    {
        var options = new RedisCircuitBreakerOptions();

        Assert.False(options.Enabled);
        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(10), options.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakDuration);
        Assert.Equal(3, options.MinimumThroughput);
    }

    [Fact]
    public void RedisCircuitBreakerOptions_PropertiesAreSettable()
    {
        var options = new RedisCircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 10,
            SamplingDuration = TimeSpan.FromSeconds(20),
            BreakDuration = TimeSpan.FromMinutes(1),
            MinimumThroughput = 7,
        };

        Assert.True(options.Enabled);
        Assert.Equal(10, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(20), options.SamplingDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), options.BreakDuration);
        Assert.Equal(7, options.MinimumThroughput);
    }

    [Fact]
    public void AddRedisCircuitBreaker_Disabled_DoesNotRegisterResiliencePipeline()
    {
        var services = new ServiceCollection();

        services.AddRedisCircuitBreaker(); // Enabled = false by default

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ResiliencePipeline));
    }

    [Fact]
    public void AddRedisCircuitBreaker_Enabled_RegistersResiliencePipelineAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRedisCircuitBreaker(o =>
        {
            o.Enabled = true;
            o.BreakDuration = TimeSpan.FromMilliseconds(500);
        });

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ResiliencePipeline));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void AddRedisCircuitBreaker_EnabledTrue_ResolvesNonNullPipeline()
    {
        var services = new ServiceCollection();
        services.AddRedisCircuitBreaker(o => o.Enabled = true);

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetService<ResiliencePipeline>();

        Assert.NotNull(pipeline);
    }

    [Fact]
    public void AddRedisCircuitBreaker_EnabledFalse_ResolvesNullPipeline()
    {
        var services = new ServiceCollection();
        services.AddRedisCircuitBreaker();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetService<ResiliencePipeline>();

        Assert.Null(pipeline);
    }

    [Fact]
    public void AddRedisCircuitBreaker_CalledTwiceWithEnabled_RegistersOnlyOnePipeline()
    {
        var services = new ServiceCollection();

        services.AddRedisCircuitBreaker(o => o.Enabled = true);
        services.AddRedisCircuitBreaker(o => o.Enabled = true);

        var count = services.Count(d => d.ServiceType == typeof(ResiliencePipeline));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddRedisCircuitBreaker_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddRedisCircuitBreaker());
    }

    [Fact]
    public void AddRedisCircuitBreaker_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddRedisCircuitBreaker();

        Assert.Same(services, result);
    }
}
