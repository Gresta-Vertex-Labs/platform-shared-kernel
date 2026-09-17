using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using SharedKernel.Caching.Redis.DistributedLocking.Implementations;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests.DI;

/// <summary>
/// Registration contract of both <c>AddRedisDistributedLocking</c> overloads.
/// </summary>
public sealed class RedisDistributedLockingExtensionsTests
{
    [Fact]
    public void CachingBuilderOverload_RegistersLockServiceAndTimeProvider()
    {
        var services = WithConnection();
        var builder = new TestCachingBuilder(services);

        var returned = builder.AddRedisDistributedLocking();

        Assert.Same(builder, returned);
        AssertLockingRegistered(services);
    }

    [Fact]
    public void ServiceCollectionOverload_RegistersLockServiceAndTimeProvider()
    {
        var services = WithConnection();

        var returned = services.AddRedisDistributedLocking();

        Assert.Same(services, returned);
        AssertLockingRegistered(services);
    }

    [Fact]
    public void BothOverloads_RegisterTheSameServices()
    {
        var viaBuilder = WithConnection();
        new TestCachingBuilder(viaBuilder).AddRedisDistributedLocking();

        var viaServices = WithConnection();
        viaServices.AddRedisDistributedLocking();

        Assert.Equal(
            viaBuilder.Select(sd => (sd.ServiceType, sd.Lifetime)),
            viaServices.Select(sd => (sd.ServiceType, sd.Lifetime)));
    }

    [Fact]
    public void ServiceCollectionOverload_WithoutConnection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRedisDistributedLocking());

        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Contains(nameof(RedisDistributedLockingExtensions.AddRedisDistributedLocking), ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void CachingBuilderOverload_WithoutConnection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => new TestCachingBuilder(services).AddRedisDistributedLocking());

        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void PreRegisteredTimeProvider_IsKept()
    {
        var services = WithConnection();
        var timeProvider = new StubTimeProvider();
        services.AddSingleton<TimeProvider>(timeProvider);

        services.AddRedisDistributedLocking();

        var descriptor = Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Same(timeProvider, descriptor.ImplementationInstance);
    }

    [Fact]
    public void CalledRepeatedlyThroughBothOverloads_DoesNotDuplicateRegistrations()
    {
        var services = WithConnection();
        var builder = new TestCachingBuilder(services);

        builder.AddRedisDistributedLocking();
        var countAfterFirst = services.Count;
        services.AddRedisDistributedLocking();
        builder.AddRedisDistributedLocking();
        services.AddRedisDistributedLocking();

        Assert.Equal(countAfterFirst, services.Count);
        Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
        Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void NullBuilderOrServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((ICachingBuilder)null!).AddRedisDistributedLocking());
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRedisDistributedLocking());
    }

    private static IServiceCollection WithConnection() =>
        new ServiceCollection().AddRedisConnection(o => o.ConnectionString = "localhost:6379");

    private static void AssertLockingRegistered(IServiceCollection services)
    {
        var lockService = Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
        Assert.Equal(ServiceLifetime.Singleton, lockService.Lifetime);
        Assert.Equal(typeof(RedisDistributedLockService), lockService.ImplementationType);

        var timeProvider = Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, timeProvider.ImplementationInstance);
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
        services
            .AddRedisConnection(o => o.ConnectionString = fixture.ConnectionString)
            .AddRedisDistributedLocking();
        await using var provider = services.BuildServiceProvider();

        var lockService = provider.GetRequiredService<IDistributedLockService>();
        Assert.IsType<RedisDistributedLockService>(lockService);
        Assert.Same(lockService, provider.GetRequiredService<IDistributedLockService>());
        Assert.Null(provider.GetService<ICacheService>());

        await using var handle = await lockService.TryAcquireAsync(RedisFixture.NewResource("service-collection"));
        Assert.NotNull(handle);
        Assert.True(handle.FencingToken > 0);
    }
}
