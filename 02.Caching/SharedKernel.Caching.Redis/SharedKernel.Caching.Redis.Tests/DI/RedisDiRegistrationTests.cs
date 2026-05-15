using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.Abstractions;
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
}
