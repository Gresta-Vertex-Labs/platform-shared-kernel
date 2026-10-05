using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.DI;

/// <summary>
/// Verifies that <see cref="CachingServiceCollectionExtensions.AddSharedKernelCaching"/>
/// registers all required services correctly and validates <see cref="CachingOptions"/>.
/// </summary>
public sealed class CachingDiRegistrationTests
{
    [Fact]
    public void AddSharedKernelCaching_RegistersICacheService()
    {
        using var provider = BuildProvider(o => o.ServiceName = "test-svc");

        Assert.NotNull(provider.GetService<ICacheService>());
    }

    [Fact]
    public void AddSharedKernelCaching_ReturnsCachingBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<ICachingBuilder>(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddSharedKernelCaching_WithCustomOptions_AppliesOptions()
    {
        using var provider = BuildProvider(o =>
        {
            o.ServiceName = "test-svc";
            o.L1SizeLimit = 500;
            o.WaitForWarmup = true;
        });

        var options = provider.GetRequiredService<IOptions<CachingOptions>>().Value;

        Assert.Equal(500, options.L1SizeLimit);
        Assert.True(options.WaitForWarmup);
        Assert.NotNull(provider.GetService<ICacheService>());
    }

    [Fact]
    public void AddSharedKernelCaching_ICacheService_IsSingleton()
    {
        using var provider = BuildProvider(o => o.ServiceName = "test-svc");

        Assert.Same(provider.GetRequiredService<ICacheService>(), provider.GetRequiredService<ICacheService>());
    }

    [Fact]
    public void AddSharedKernelCaching_CalledTwice_DoesNotDuplicateICacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc"); // idempotent via TryAdd

        Assert.Single(services, d => d.ServiceType == typeof(ICacheService));
        Assert.Single(services, d => d.ServiceType == typeof(ICacheKeyProvider));
        Assert.Single(services, d => d.ServiceType == typeof(ITenantCacheKeyProvider));
    }

    // -------------------------------------------------------------------------
    // CachingOptions.ServiceName validation
    // -------------------------------------------------------------------------

    [Fact]
    public void ServiceName_NotConfigured_FailsOnOptionsResolution()
    {
        using var provider = BuildProvider(_ => { });

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<CachingOptions>>().Value);

        Assert.Contains("ServiceName", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Order-Service")]
    [InlineData("order service")]
    [InlineData("order:svc")]
    [InlineData("-order")]
    public void ServiceName_Invalid_FailsStartupValidation(string serviceName)
    {
        using var provider = BuildProvider(o => o.ServiceName = serviceName);

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("ServiceName", ex.Message);
    }

    [Fact]
    public void ServiceName_Invalid_KeyProviderResolutionFails()
    {
        using var provider = BuildProvider(o => o.ServiceName = "Not Valid");

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ICacheKeyProvider>());
    }

    [Fact]
    public async Task ServiceName_Invalid_HostStartFails()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSharedKernelCaching(o => o.ServiceName = "");

        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("order-svc")]
    [InlineData("orders.api_v2")]
    [InlineData("9lives")]
    public void ServiceName_Valid_PassesStartupValidation(string serviceName)
    {
        using var provider = BuildProvider(o => o.ServiceName = serviceName);

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(serviceName, provider.GetRequiredService<IOptions<CachingOptions>>().Value.ServiceName);
    }

    [Fact]
    public void AddSharedKernelCaching_NullConfigureDelegate_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddSharedKernelCaching((Action<CachingOptions>)null!));
    }

    private static ServiceProvider BuildProvider(Action<CachingOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(configure);
        return services.BuildServiceProvider();
    }
}
