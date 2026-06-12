using Microsoft.Extensions.DependencyInjection;
using Polly;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Caching.Redis.PubSub.Tests;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests.DI;

/// <summary>
/// DI registration unit tests for <see cref="RedisChannelServiceExtensions"/> covering the
/// optional <see cref="ResiliencePipeline"/> resolution from <c>AddRedisCircuitBreaker</c>
/// (relocated from <c>SharedKernel.Caching.Redis.Tests.CircuitBreakerTests</c>, Phase 36).
/// </summary>
public sealed class RedisChannelServiceDiTests
{
    [Fact]
    public void AddRedisChannelService_WithCircuitBreakerEnabled_InjectsResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");
        services.AddRedisCircuitBreaker(o => o.Enabled = true);

        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRedisChannelService));
        Assert.NotNull(descriptor);
        Assert.Contains(services, d => d.ServiceType == typeof(ResiliencePipeline));
    }

    [Fact]
    public void AddRedisChannelService_WithoutCircuitBreaker_ResolvesWithNullPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");
        // AddRedisCircuitBreaker not called — ResiliencePipeline must not be registered.

        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();

        using var provider = services.BuildServiceProvider();

        var channelService = provider.GetService<IRedisChannelService>();
        Assert.NotNull(channelService);

        var pipeline = provider.GetService<ResiliencePipeline>();
        Assert.Null(pipeline);
    }
}
