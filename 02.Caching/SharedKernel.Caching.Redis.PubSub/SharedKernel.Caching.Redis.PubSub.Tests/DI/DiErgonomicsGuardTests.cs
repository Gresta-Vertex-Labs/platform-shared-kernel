using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Caching.Redis.PubSub.Tests;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests.DI;

/// <summary>
/// Unit tests for the DI guard on <c>AddRedisChannelService</c> when <c>IConnectionMultiplexer</c> is absent.
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
}
