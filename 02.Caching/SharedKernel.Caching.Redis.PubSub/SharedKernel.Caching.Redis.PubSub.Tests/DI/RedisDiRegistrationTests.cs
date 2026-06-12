using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.PubSub.Extensions;
using SharedKernel.Caching.Redis.PubSub.Tests;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests.DI;

/// <summary>
/// Verifies DI registration contracts for Redis caching services.
/// </summary>
public sealed class RedisDiRegistrationTests
{
    [Fact]
    public void AddRedisChannelService_RegistersIRedisChannelService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();

        using var provider = services.BuildServiceProvider();
        var channelService = provider.GetService<IRedisChannelService>();

        Assert.NotNull(channelService);
    }

    [Fact]
    public void AddRedisChannelService_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection("localhost:6379");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisChannelService();
        builder.AddRedisChannelService(); // idempotent

        var registrations = services
            .Where(d => d.ServiceType == typeof(IRedisChannelService))
            .ToList();

        Assert.Single(registrations);
    }

    [Fact]
    public void AddRedisChannelService_NullBuilder_ThrowsArgumentNullException()
    {
        ICachingBuilder? nullBuilder = null;
        Assert.Throws<ArgumentNullException>(() => nullBuilder!.AddRedisChannelService());
    }
}
