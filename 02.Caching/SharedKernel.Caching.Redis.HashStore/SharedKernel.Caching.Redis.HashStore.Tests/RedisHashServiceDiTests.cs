using Microsoft.Extensions.DependencyInjection;
using Polly;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.HashStore.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>
/// DI registration unit tests for <see cref="RedisHashServiceExtensions"/>.
/// Covers RHS-04 (startup guard message referencing <c>AddRedisConnection</c>) and
/// RHS-05 (optional <see cref="ResiliencePipeline"/> resolution from
/// <c>AddRedisCircuitBreaker</c>).
/// </summary>
public sealed class RedisHashServiceDiTests
{
    [Fact]
    public void AddRedisHashService_RegistersIRedisHashService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();

        using var provider = services.BuildServiceProvider();
        var hashService = provider.GetService<IRedisHashService>();

        Assert.NotNull(hashService);
    }

    [Fact]
    public void AddRedisHashService_WithoutMultiplexer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddRedisHashService());

        Assert.Contains("AddRedisHashService requires", ex.Message);
        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Contains("IConnectionMultiplexer", ex.Message);
    }

    [Fact]
    public void AddRedisHashService_NullBuilder_ThrowsArgumentNullException()
    {
        ICachingBuilder? nullBuilder = null;
        Assert.Throws<ArgumentNullException>(() => nullBuilder!.AddRedisHashService());
    }

    [Fact]
    public void AddRedisHashService_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();
        builder.AddRedisHashService(); // idempotent

        var registrations = services
            .Where(d => d.ServiceType == typeof(IRedisHashService))
            .ToList();

        Assert.Single(registrations);
    }

    // ─── RHS-05: optional ResiliencePipeline resolution from AddRedisCircuitBreaker ─

    [Fact]
    public void AddRedisHashService_WithCircuitBreakerEnabled_InjectsResiliencePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");
        services.AddRedisCircuitBreaker(o => o.Enabled = true);

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();

        using var provider = services.BuildServiceProvider();

        var hashService = provider.GetService<IRedisHashService>();
        Assert.NotNull(hashService);

        var pipeline = provider.GetService<ResiliencePipeline>();
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void AddRedisHashService_WithoutCircuitBreaker_ResolvesWithNullPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");
        // AddRedisCircuitBreaker not called — ResiliencePipeline must not be registered.

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();

        using var provider = services.BuildServiceProvider();

        var hashService = provider.GetService<IRedisHashService>();
        Assert.NotNull(hashService);

        var pipeline = provider.GetService<ResiliencePipeline>();
        Assert.Null(pipeline);
    }
}
