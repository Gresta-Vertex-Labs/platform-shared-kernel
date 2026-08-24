using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Core.Extensions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionCoreExtensions.AddRedisConnection"/>.
/// Covers RC-05: connection registration and first-caller-wins semantics.
/// </summary>
public sealed class RedisConnectionCoreExtensionsTests
{
    [Fact]
    public void AddRedisConnection_RegistersConnectionMultiplexerAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379");

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void AddRedisConnection_RegistersConnectionHealthTrackerAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379");

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(RedisConnectionHealthTracker));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void AddRedisConnection_CalledTwice_RegistersConnectionMultiplexerOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379");
        services.AddRedisConnection("localhost:6380"); // second caller — should be a no-op

        var count = services.Count(d => d.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddRedisConnection_CalledTwice_RegistersConnectionHealthTrackerOnlyOnce()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379");
        services.AddRedisConnection("localhost:6380");

        var count = services.Count(d => d.ServiceType == typeof(RedisConnectionHealthTracker));
        Assert.Equal(1, count);
    }

    [Fact]
    public void AddRedisConnection_NullConnectionString_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddRedisConnection(null!));
    }

    [Fact]
    public void AddRedisConnection_WhitespaceConnectionString_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddRedisConnection("   "));
    }

    [Fact]
    public void AddRedisConnection_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddRedisConnection("localhost:6379"));
    }

    [Fact]
    public void AddRedisConnection_WithConfigureDelegate_AppliesConnectTimeout()
    {
        // Phase 45 (WO-065/P-436): RedisConnectionOptions is now registered via
        // services.AddOptions<RedisConnectionOptions>().Configure(...) — the configure delegate is
        // invoked lazily, when IOptions<RedisConnectionOptions>.Value is first resolved, rather than
        // synchronously inside AddRedisConnection itself. This test observes the applied value via a
        // real DI resolution instead of a side-channel capture, matching that (correct) semantics.
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379", o => o.ConnectTimeoutMs = 1234);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal(1234, resolved.ConnectTimeoutMs);
        Assert.Single(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void AddRedisConnection_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddRedisConnection("localhost:6379");

        Assert.Same(services, result);
    }
}
