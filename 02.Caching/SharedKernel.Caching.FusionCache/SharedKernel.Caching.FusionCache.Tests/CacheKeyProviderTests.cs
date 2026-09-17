using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Tests for <see cref="CacheKeyProvider"/> — that it applies the configured service name to the
/// shared <see cref="CacheKeyFormat"/>, and that <see cref="CachingServiceCollectionExtensions.AddSharedKernelCaching"/>
/// registers one instance for both key-provider interfaces.
/// </summary>
/// <remarks>
/// The key format itself (escaping, tenant marker, collision rules) is covered by
/// <c>SharedKernel.Caching.Abstractions.Tests</c>; these tests only verify the provider's mapping onto it.
/// </remarks>
public sealed class CacheKeyProviderTests
{
    // -------------------------------------------------------------------------
    // Mapping onto CacheKeyFormat
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildKey_UsesConfiguredServiceName()
    {
        var provider = CreateProvider("order-svc");

        Assert.Equal("order-svc:invoice:42", provider.BuildKey("invoice", "42"));
        Assert.Equal(CacheKeyFormat.BuildKey("order-svc", "invoice", "42", "en-GB"), provider.BuildKey("invoice", "42", "en-GB"));
    }

    [Fact]
    public void BuildTenantKey_UsesConfiguredServiceName()
    {
        var provider = CreateProvider("order-svc");

        Assert.Equal("order-svc:@tenant-a:invoice:42", provider.BuildTenantKey("tenant-a", "invoice", "42"));
        Assert.Equal(
            CacheKeyFormat.BuildTenantKey("order-svc", "tenant-a", "invoice", "42", "en-GB"),
            provider.BuildTenantKey("tenant-a", "invoice", "42", "en-GB"));
    }

    [Fact]
    public void BuildTenantKey_DifferentTenants_ProduceDifferentKeys_AndNeverTheGlobalKey()
    {
        var provider = CreateProvider("order-svc");

        var keyA = provider.BuildTenantKey("tenant-a", "invoice", "42");
        var keyB = provider.BuildTenantKey("tenant-b", "invoice", "42");
        var global = provider.BuildKey("invoice", "42");

        Assert.NotEqual(keyA, keyB);
        Assert.NotEqual(global, keyA);
        Assert.NotEqual(global, keyB);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_NullOrWhitespaceEntityOrId_ThrowsArgumentException(string value)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildKey(value, "123"));
        Assert.Throws<ArgumentException>(() => provider.BuildKey("entity", value));
        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey(value, "entity", "id"));
    }

    // -------------------------------------------------------------------------
    // DI registration
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelCaching_OneInstanceServesBothKeyProviderInterfaces()
    {
        using var provider = BuildProvider("test-svc");

        var keyProvider = provider.GetRequiredService<ICacheKeyProvider>();
        var tenantKeyProvider = provider.GetRequiredService<ITenantCacheKeyProvider>();

        Assert.IsType<CacheKeyProvider>(keyProvider);
        Assert.Same(keyProvider, tenantKeyProvider);
        Assert.Same(keyProvider, provider.GetRequiredService<ICacheKeyProvider>());
    }

    [Fact]
    public void AddSharedKernelCaching_KeyProviders_UseServiceNameFromOptions()
    {
        using var provider = BuildProvider("resolved-svc");

        Assert.Equal("resolved-svc:order:7", provider.GetRequiredService<ICacheKeyProvider>().BuildKey("order", "7"));
        Assert.Equal(
            "resolved-svc:@t1:order:7",
            provider.GetRequiredService<ITenantCacheKeyProvider>().BuildTenantKey("t1", "order", "7"));
    }

    [Fact]
    public void AddSharedKernelCaching_CustomICacheKeyProvider_OverridesDefault()
    {
        // A consumer registration after AddSharedKernelCaching wins (standard DI behavior).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        services.AddSingleton<ICacheKeyProvider>(new CustomKeyProvider());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CustomKeyProvider>(provider.GetRequiredService<ICacheKeyProvider>());
        Assert.IsType<CacheKeyProvider>(provider.GetRequiredService<ITenantCacheKeyProvider>());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ITenantCacheKeyProvider CreateProvider(string serviceName) =>
        new CacheKeyProvider(Options.Create(new CachingOptions { ServiceName = serviceName }));

    private static ServiceProvider BuildProvider(string serviceName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = serviceName);
        return services.BuildServiceProvider();
    }

    private sealed class CustomKeyProvider : ICacheKeyProvider
    {
        public string BuildKey(string entity, string id, params string[] segments) => $"custom:{entity}:{id}";
    }
}
