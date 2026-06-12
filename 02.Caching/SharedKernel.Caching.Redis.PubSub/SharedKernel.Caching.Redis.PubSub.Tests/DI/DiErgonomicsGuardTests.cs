using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Caching.Redis.PubSub.Tests;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests.DI;

/// <summary>
/// Unit tests for Phase 19 DI ergonomics hardening:
/// <list type="bullet">
///   <item><description>Guard on <c>AddRedisChannelService</c> when <c>IConnectionMultiplexer</c> is absent.</description></item>
///   <item><description>Guards on <c>AddCacheInvalidationReceiver</c> when <c>IRedisChannelService</c> or <c>ICacheService</c> is absent.</description></item>
/// </list>
/// </summary>
public sealed class DiErgonomicsGuardTests
{
    // -------------------------------------------------------------------------
    // AddRedisChannelService — IConnectionMultiplexer guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddRedisChannelService_WithoutMultiplexer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddRedisChannelService());

        Assert.Equal(
            "AddRedisChannelService requires AddRedisConnection (directly, or transitively via AddRedisL2 / AddRedisDistributedLocking / AddRedisHashService) to be called first to register IConnectionMultiplexer.",
            ex.Message);
    }

    [Fact]
    public void AddRedisChannelService_WithMultiplexerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        // Register a fake IConnectionMultiplexer to satisfy the guard.
        services.AddSingleton<IConnectionMultiplexer>(_ => null!);
        var builder = new TestCachingBuilder(services);

        // Should not throw.
        var returned = builder.AddRedisChannelService();

        Assert.Same(builder, returned);
        Assert.Single(services, sd => sd.ServiceType == typeof(IRedisChannelService));
    }

    // -------------------------------------------------------------------------
    // AddCacheInvalidationReceiver — IRedisChannelService guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheInvalidationReceiver_WithoutChannelService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Register ICacheService but NOT IRedisChannelService.
        services.AddSingleton<ICacheService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheInvalidationReceiver());

        Assert.Equal(
            "AddCacheInvalidationReceiver requires AddRedisChannelService to be called first.",
            ex.Message);
    }

    // -------------------------------------------------------------------------
    // AddCacheInvalidationReceiver — ICacheService guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheInvalidationReceiver_WithoutCacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Register IRedisChannelService but NOT ICacheService.
        services.AddSingleton<IRedisChannelService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheInvalidationReceiver());

        Assert.Equal(
            "AddCacheInvalidationReceiver requires AddSharedKernelCaching to be called first to register ICacheService.",
            ex.Message);
    }

    [Fact]
    public void AddCacheInvalidationReceiver_WithBothDependencies_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRedisChannelService>(_ => null!);
        services.AddSingleton<ICacheService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        // Should not throw — both required registrations are present.
        var returned = builder.AddCacheInvalidationReceiver();

        Assert.Same(builder, returned);
    }
}
