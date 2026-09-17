using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests.DI;

/// <summary>
/// Registration contract of <see cref="RedisChannelServiceExtensions.AddRedisChannelService"/>.
/// </summary>
public sealed class RedisChannelServiceExtensionsTests
{
    [Fact]
    public void AddRedisChannelService_RegistersSingletonChannelService()
    {
        var services = WithConnection();

        var returned = services.AddRedisChannelService();

        Assert.Same(services, returned);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IRedisChannelService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(RedisChannelService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddRedisChannelService_ResolvesSameInstance()
    {
        var services = WithConnection();
        services.AddLogging();
        services.AddRedisChannelService();

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IRedisChannelService>(), provider.GetRequiredService<IRedisChannelService>());
    }

    [Fact]
    public void AddRedisChannelService_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = WithConnection();

        services.AddRedisChannelService();
        var count = services.Count;
        services.AddRedisChannelService();

        Assert.Equal(count, services.Count);
        Assert.Single(services, d => d.ServiceType == typeof(IRedisChannelService));
    }

    [Fact]
    public void AddRedisChannelService_WithoutConnection_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRedisChannelService());

        Assert.Contains("AddRedisConnection", ex.Message);
        Assert.Contains(nameof(RedisChannelServiceExtensions.AddRedisChannelService), ex.Message);
        Assert.Empty(services);
    }

    [Fact]
    public void AddRedisChannelService_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRedisChannelService());
    }

    private static IServiceCollection WithConnection() =>
        new ServiceCollection().AddRedisConnection(o => o.ConnectionString = "localhost:6379");
}
