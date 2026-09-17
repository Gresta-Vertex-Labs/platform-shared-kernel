using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Caching.Redis.PubSub;
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
    public void AddFakeCachingServices_BothKeyProviderInterfaces_ResolveToSameInstance()
    {
        var provider = BuildProvider();

        var keyProvider = provider.GetRequiredService<ICacheKeyProvider>();
        var tenantKeyProvider = provider.GetRequiredService<ITenantCacheKeyProvider>();

        Assert.IsType<FakeTenantCacheKeyProvider>(keyProvider);
        Assert.Same(keyProvider, tenantKeyProvider);
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
    public void AddFakeCachingServices_RegistersNoCacheInvalidationBus()
    {
        var services = new ServiceCollection();
        services.AddFakeCachingServices();

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("Invalidation", StringComparison.Ordinal)
                || (descriptor.ImplementationType?.Name.Contains("Invalidation", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public void AddFakeCachingServices_CoreServicesAreSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<ICacheService>(), provider.GetRequiredService<ICacheService>());
        Assert.Same(provider.GetRequiredService<IDistributedLockService>(), provider.GetRequiredService<IDistributedLockService>());
        Assert.Same(provider.GetRequiredService<ITenantCacheKeyProvider>(), provider.GetRequiredService<ITenantCacheKeyProvider>());
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
