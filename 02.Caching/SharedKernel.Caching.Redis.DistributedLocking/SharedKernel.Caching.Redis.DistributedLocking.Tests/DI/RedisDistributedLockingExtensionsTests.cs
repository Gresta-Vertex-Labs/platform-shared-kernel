using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests.DI;

/// <summary>
/// Registration contract of both <c>AddRedisDistributedLocking</c> overloads.
/// </summary>
public sealed class RedisDistributedLockingExtensionsTests
{
    private const string ConnectionString = "localhost:6379";

    [Fact]
    public void CachingBuilderOverload_RegistersLockServiceTimeProviderAndConnection()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var returned = builder.AddRedisDistributedLocking(ConnectionString);

        Assert.Same(builder, returned);
        AssertLockingRegistered(services);
    }

    [Fact]
    public void ServiceCollectionOverload_RegistersLockServiceTimeProviderAndConnection()
    {
        var services = new ServiceCollection();

        var returned = services.AddRedisDistributedLocking(ConnectionString);

        Assert.Same(services, returned);
        AssertLockingRegistered(services);
    }

    [Fact]
    public void BothOverloads_RegisterTheSameServices()
    {
        var viaBuilder = new ServiceCollection();
        new TestCachingBuilder(viaBuilder).AddRedisDistributedLocking(ConnectionString);

        var viaServices = new ServiceCollection();
        viaServices.AddRedisDistributedLocking(ConnectionString);

        Assert.Equal(
            viaBuilder.Select(sd => (sd.ServiceType, sd.Lifetime)),
            viaServices.Select(sd => (sd.ServiceType, sd.Lifetime)));
    }

    [Fact]
    public void PreRegisteredTimeProvider_IsKept()
    {
        var services = new ServiceCollection();
        var timeProvider = new StubTimeProvider();
        services.AddSingleton<TimeProvider>(timeProvider);

        services.AddRedisDistributedLocking(ConnectionString);

        var descriptor = Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Same(timeProvider, descriptor.ImplementationInstance);
    }

    [Fact]
    public void CalledTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        builder.AddRedisDistributedLocking(ConnectionString);
        services.AddRedisDistributedLocking(ConnectionString);

        Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
        Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Single(services, sd => sd.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void Configure_ConnectTimeoutFlowsToConnectionOptions()
    {
        var services = new ServiceCollection();
        services.AddRedisDistributedLocking(ConnectionString, o => o.ConnectTimeoutMs = 1_234);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal(ConnectionString, options.ConnectionString);
        Assert.Equal(1_234, options.ConnectTimeoutMs);
    }

    [Fact]
    public void NullBuilderOrServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((ICachingBuilder)null!).AddRedisDistributedLocking(ConnectionString));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRedisDistributedLocking(ConnectionString));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankConnectionString_ThrowsArgumentException(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new TestCachingBuilder(new ServiceCollection()).AddRedisDistributedLocking(connectionString));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRedisDistributedLocking(connectionString));
    }

    private static void AssertLockingRegistered(IServiceCollection services)
    {
        var lockService = Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
        Assert.Equal(ServiceLifetime.Singleton, lockService.Lifetime);
        Assert.Equal(typeof(RedisDistributedLockService), lockService.ImplementationType);

        var timeProvider = Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, timeProvider.ImplementationInstance);

        Assert.Single(services, sd => sd.ServiceType == typeof(IConnectionMultiplexer));
    }

    private sealed class StubTimeProvider : TimeProvider;
}

/// <summary>
/// Resolves and uses the lock service registered without a caching builder, against a real Redis server.
/// </summary>
[Collection("Redis")]
public sealed class RedisDistributedLockingServiceCollectionTests(RedisFixture fixture)
{
    [Fact]
    public async Task ServiceCollectionOverload_ResolvesWorkingLockServiceWithoutCaching()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking(fixture.ConnectionString);
        await using var provider = services.BuildServiceProvider();

        var lockService = provider.GetRequiredService<IDistributedLockService>();
        Assert.IsType<RedisDistributedLockService>(lockService);
        Assert.Null(provider.GetService<ICacheService>());

        await using var handle = await lockService.TryAcquireAsync(RedisFixture.NewResource("service-collection"));
        Assert.NotNull(handle);
        Assert.True(handle.FencingToken > 0);
    }
}
