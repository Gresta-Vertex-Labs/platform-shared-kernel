using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

public sealed class AddFakeCachingServicesTests
{
    [Fact]
    public void AddFakeCachingServices_RegistersFakeCacheService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeCacheService>(provider.GetRequiredService<ICacheService>());
    }

    [Fact]
    public void AddFakeCachingServices_RegistersFakeDistributedLockService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeDistributedLockService>(provider.GetRequiredService<IDistributedLockService>());
    }

    [Fact]
    public void AddFakeCachingServices_RegistersFakeTenantCacheKeyProvider()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeTenantCacheKeyProvider>(provider.GetRequiredService<ITenantCacheKeyProvider>());
    }

    [Fact]
    public void AddFakeCachingServices_RegistersFakeCacheInvalidationBus()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeCacheInvalidationBus>(provider.GetRequiredService<ICacheInvalidationBus>());
    }

    [Fact]
    public void AddFakeCachingServices_RegistersFakeRedisChannelService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeRedisChannelService>(provider.GetRequiredService<IRedisChannelService>());
    }

    [Fact]
    public void AddFakeCachingServices_RegistersFakeRedisHashService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeRedisHashService>(provider.GetRequiredService<IRedisHashService>());
    }

    [Fact]
    public void AddFakeCachingServices_AllFourAreSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<ICacheService>(), provider.GetRequiredService<ICacheService>());
        Assert.Same(provider.GetRequiredService<IDistributedLockService>(), provider.GetRequiredService<IDistributedLockService>());
        Assert.Same(provider.GetRequiredService<ITenantCacheKeyProvider>(), provider.GetRequiredService<ITenantCacheKeyProvider>());
        Assert.Same(provider.GetRequiredService<ICacheInvalidationBus>(), provider.GetRequiredService<ICacheInvalidationBus>());
    }

    [Fact]
    public void AddFakeCachingServices_RedisChannelAndHashServicesAreAlsoSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IRedisChannelService>(), provider.GetRequiredService<IRedisChannelService>());
        Assert.Same(provider.GetRequiredService<IRedisHashService>(), provider.GetRequiredService<IRedisHashService>());
    }

    [Fact]
    public void AddFakeCachingServices_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeCachingServices());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeCachingServices();
        return services.BuildServiceProvider();
    }
}
