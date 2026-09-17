using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Core.Health;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Registration behaviour of both <c>AddRedisConnection</c> overloads and
/// <see cref="RedisConnectionCoreExtensions.EnsureRedisConnectionRegistered"/>.
/// </summary>
public sealed class RedisConnectionCoreExtensionsTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public void DelegateOverload_RegistersMultiplexerAndProbeAsSingletons()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IConnectionMultiplexer)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IRedisConnectionProbe)).Lifetime);
    }

    [Fact]
    public void ConfigurationOverload_RegistersMultiplexerAndProbeAsSingletons()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection(EmptyConfiguration());

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IConnectionMultiplexer)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IRedisConnectionProbe)).Lifetime);
    }

    [Fact]
    public void BothOverloads_ReturnTheSameServiceCollection()
    {
        var first = new ServiceCollection();
        var second = new ServiceCollection();

        Assert.Same(first, first.AddRedisConnection(o => o.ConnectionString = "localhost:6379"));
        Assert.Same(second, second.AddRedisConnection(EmptyConfiguration()));
    }

    [Fact]
    public void DelegateOverload_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddRedisConnection(o => o.ConnectionString = "localhost:6380"));

        Assert.Contains("already registered", exception.Message);
        Assert.Single(services, d => d.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void ConfigurationOverloadAfterDelegateOverload_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");

        Assert.Throws<InvalidOperationException>(() => services.AddRedisConnection(EmptyConfiguration()));
    }

    [Fact]
    public void DelegateOverloadAfterConfigurationOverload_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(EmptyConfiguration());

        Assert.Throws<InvalidOperationException>(() => services.AddRedisConnection(o => o.ConnectionString = "localhost:6379"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        IServiceCollection nullServices = null!;
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisConnection(o => o.ConnectionString = "localhost:6379"));
        Assert.Throws<ArgumentNullException>(() => nullServices.AddRedisConnection(EmptyConfiguration()));
        Assert.Throws<ArgumentNullException>(() => services.AddRedisConnection((Action<RedisConnectionOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddRedisConnection((IConfiguration)null!));
        Assert.Empty(services);
    }

    [Fact]
    public void EnsureRedisConnectionRegistered_NotRegistered_ThrowsNamingTheCaller()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.EnsureRedisConnectionRegistered("AddSomethingOnRedis"));

        Assert.Contains("AddSomethingOnRedis", exception.Message);
        Assert.Contains("AddRedisConnection", exception.Message);
    }

    [Fact]
    public void EnsureRedisConnectionRegistered_RegisteredWithDelegate_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");

        services.EnsureRedisConnectionRegistered("AddSomethingOnRedis");
    }

    [Fact]
    public void EnsureRedisConnectionRegistered_RegisteredWithConfiguration_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(EmptyConfiguration());

        services.EnsureRedisConnectionRegistered("AddSomethingOnRedis");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureRedisConnectionRegistered_BlankCaller_Throws(string? caller)
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");

        Assert.ThrowsAny<ArgumentException>(() => services.EnsureRedisConnectionRegistered(caller!));
    }

    [Fact]
    public void EnsureRedisConnectionRegistered_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.EnsureRedisConnectionRegistered("caller"));
    }

    [Fact]
    public void DelegateOverload_AppliesTheDelegateWhenOptionsAreResolved()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o =>
        {
            o.ConnectionString = "redis.internal:6380";
            o.ConnectTimeout = TimeSpan.FromMilliseconds(1234);
            o.FailFastWhenDisconnected = false;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal("redis.internal:6380", options.ConnectionString);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), options.ConnectTimeout);
        Assert.False(options.FailFastWhenDisconnected);
    }
}
