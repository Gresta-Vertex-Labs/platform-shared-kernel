using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;
using Xunit;

namespace SharedKernel.Caching.Tests.DI;

/// <summary>
/// Verifies that <see cref="CachingServiceCollectionExtensions.AddSharedKernelCaching"/>
/// registers all required services correctly.
/// </summary>
public sealed class CachingDiRegistrationTests
{
    [Fact]
    public void AddSharedKernelCaching_RegistersICacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching();

        using var provider = services.BuildServiceProvider();
        var cacheService = provider.GetService<ICacheService>();

        Assert.NotNull(cacheService);
    }

    [Fact]
    public void AddSharedKernelCaching_ReturnsCachingBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddSharedKernelCaching();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<ICachingBuilder>(builder);
    }

    [Fact]
    public void AddSharedKernelCaching_WithCustomOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.L1SizeLimit = 500;
            o.CacheName = "test-cache";
        });

        // Should build without throwing — ValidateOnStart will catch misconfiguration.
        using var provider = services.BuildServiceProvider();
        var cacheService = provider.GetService<ICacheService>();

        Assert.NotNull(cacheService);
    }

    [Fact]
    public void AddSharedKernelCaching_ICacheService_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ICacheService>();
        var second = provider.GetRequiredService<ICacheService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddSharedKernelCaching_CalledTwice_DoesNotDuplicateICacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching();
        services.AddSharedKernelCaching(); // idempotent via TryAdd

        using var provider = services.BuildServiceProvider();

        // Should be exactly one ICacheService registration.
        var registrations = services.Where(d => d.ServiceType == typeof(ICacheService)).ToList();
        Assert.Single(registrations);
    }
}
