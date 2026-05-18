using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.DI;

/// <summary>
/// Verifies DI registration contracts for Redis caching services.
/// </summary>
public sealed class RedisDiRegistrationTests
{
    [Fact]
    public void AddRedisDistributedLocking_RegistersIDistributedLockService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking("localhost:6379");

        using var provider = services.BuildServiceProvider();
        var lockService = provider.GetService<IDistributedLockService>();

        Assert.NotNull(lockService);
    }

    [Fact]
    public void AddRedisDistributedLocking_IDistributedLockService_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking("localhost:6379");

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IDistributedLockService>();
        var second = provider.GetRequiredService<IDistributedLockService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddRedisDistributedLocking_CalledTwice_DoesNotDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking("localhost:6379");
        services.AddRedisDistributedLocking("localhost:6379"); // idempotent

        var registrations = services
            .Where(d => d.ServiceType == typeof(IDistributedLockService))
            .ToList();

        Assert.Single(registrations);
    }

    [Fact]
    public void AddRedisDistributedLocking_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? nullServices = null;

        Assert.Throws<ArgumentNullException>(() =>
            nullServices!.AddRedisDistributedLocking("localhost:6379"));
    }

    [Fact]
    public void AddRedisDistributedLocking_EmptyConnectionString_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddRedisDistributedLocking(""));

        Assert.Throws<ArgumentException>(() =>
            services.AddRedisDistributedLocking("   "));
    }

    [Fact]
    public void AddRedisChannelService_RegistersIRedisChannelService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking("localhost:6379");

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
        services.AddRedisDistributedLocking("localhost:6379");

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

    [Fact]
    public void AddRedisHashService_RegistersIRedisHashService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking("localhost:6379");

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();

        using var provider = services.BuildServiceProvider();
        var hashService = provider.GetService<IRedisHashService>();

        Assert.NotNull(hashService);
    }

    [Fact]
    public void AddRedisHashService_WithoutMultiplexer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddRedisHashService());
        Assert.Contains("IConnectionMultiplexer", ex.Message);
    }

    [Fact]
    public void AddRedisHashService_NullBuilder_ThrowsArgumentNullException()
    {
        ICachingBuilder? nullBuilder = null;
        Assert.Throws<ArgumentNullException>(() => nullBuilder!.AddRedisHashService());
    }
}
