using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests for <see cref="TenantCacheKeyProvider"/> — key format, tenant isolation,
/// consistency with <see cref="CacheKeyProvider"/>, argument validation, and DI registration
/// via <see cref="TenantCacheKeyProviderExtensions.AddTenantCacheKeyProvider"/>.
/// </summary>
public sealed class TenantCacheKeyProviderTests
{
    // -------------------------------------------------------------------------
    // BuildTenantKey — format
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildTenantKey_WithoutExtraSegments_ProducesServiceTenantEntityIdFormat()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildTenantKey("tenant-a", "invoice", "42");

        Assert.Equal("order-svc:tenant-a:invoice:42", key);
    }

    [Fact]
    public void BuildTenantKey_WithOneExtraSegment_AppendsWithColon()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildTenantKey("tenant-a", "invoice", "42", "en-GB");

        Assert.Equal("order-svc:tenant-a:invoice:42:en-GB", key);
    }

    [Fact]
    public void BuildTenantKey_WithMultipleExtraSegments_AppendsAllWithColons()
    {
        var provider = CreateProvider("my-service");

        var key = provider.BuildTenantKey("tenant-x", "user-profile", "usr-001", "en-GB", "shard-1");

        Assert.Equal("my-service:tenant-x:user-profile:usr-001:en-GB:shard-1", key);
    }

    [Fact]
    public void BuildTenantKey_ServiceNameIsFirstSegment()
    {
        var provider = CreateProvider("catalog");

        var key = provider.BuildTenantKey("tenant-a", "product", "sku-x");

        Assert.StartsWith("catalog:", key, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTenantKey_TenantIdIsSecondSegment()
    {
        var provider = CreateProvider("svc");

        var key = provider.BuildTenantKey("my-tenant", "entity", "id");

        var segments = key.Split(':');
        Assert.Equal("my-tenant", segments[1]);
    }

    [Fact]
    public void BuildTenantKey_DoesNotNormaliseCase()
    {
        var provider = CreateProvider("MyService");

        var key = provider.BuildTenantKey("TenantABC", "Entity", "ID-001");

        Assert.Equal("MyService:TenantABC:Entity:ID-001", key);
    }

    // -------------------------------------------------------------------------
    // Tenant isolation — different tenant IDs → different keys
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildTenantKey_DifferentTenantIds_ProduceDifferentKeys()
    {
        var provider = CreateProvider("order-svc");

        var keyA = provider.BuildTenantKey("tenant-a", "invoice", "42");
        var keyB = provider.BuildTenantKey("tenant-b", "invoice", "42");

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void BuildTenantKey_DifferentTenantIds_WithExtraSegments_ProduceDifferentKeys()
    {
        var provider = CreateProvider("svc");

        var keyA = provider.BuildTenantKey("tenant-1", "order", "99", "en-GB");
        var keyB = provider.BuildTenantKey("tenant-2", "order", "99", "en-GB");

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void BuildTenantKey_SameTenantId_ProducesSameKey_ForSameEntityAndId()
    {
        var provider = CreateProvider("order-svc");

        var keyA = provider.BuildTenantKey("tenant-a", "invoice", "42");
        var keyB = provider.BuildTenantKey("tenant-a", "invoice", "42");

        Assert.Equal(keyA, keyB);
    }

    // -------------------------------------------------------------------------
    // Format consistency with BuildKey (non-tenant)
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildKey_WithoutExtraSegments_ProducesServiceEntityIdFormat()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42");

        // Non-tenant key has no tenant segment — matches CacheKeyProvider format.
        Assert.Equal("order-svc:invoice:42", key);
    }

    [Fact]
    public void BuildTenantKey_VsBaseKey_TenantKeyHasAdditionalTenantSegment()
    {
        var provider = CreateProvider("order-svc");

        var baseKey = provider.BuildKey("invoice", "42");
        var tenantKey = provider.BuildTenantKey("tenant-a", "invoice", "42");

        // Tenant key inserts the tenant segment after the service name.
        Assert.Equal("order-svc:invoice:42", baseKey);
        Assert.Equal("order-svc:tenant-a:invoice:42", tenantKey);
        Assert.Contains(baseKey.Replace("order-svc:", string.Empty, StringComparison.Ordinal),
            tenantKey, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildKey_WithVersion_ProducesVersionedKey()
    {
        var provider = CreateProvider("svc");

        var key = provider.BuildKey("entity", "id", 3);

        Assert.Equal("svc:entity:id:v3", key);
    }

    [Fact]
    public void BuildKey_WithVersionZero_ProducesNoVersionSuffix()
    {
        var provider = CreateProvider("svc");

        var key = provider.BuildKey("entity", "id", 0);

        Assert.Equal("svc:entity:id", key);
    }

    // -------------------------------------------------------------------------
    // Argument validation — BuildTenantKey
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTenantKey_NullOrWhitespaceTenantId_ThrowsArgumentException(string tenantId)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey(tenantId, "entity", "id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTenantKey_NullOrWhitespaceEntity_ThrowsArgumentException(string entity)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey("tenant", entity, "id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildTenantKey_NullOrWhitespaceId_ThrowsArgumentException(string id)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey("tenant", "entity", id));
    }

    // -------------------------------------------------------------------------
    // DI registration — AddTenantCacheKeyProvider
    // -------------------------------------------------------------------------

    [Fact]
    public void AddTenantCacheKeyProvider_RegistersITenantCacheKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheKeyProvider();

        using var provider = services.BuildServiceProvider();
        var tenantKeyProvider = provider.GetService<ITenantCacheKeyProvider>();

        Assert.NotNull(tenantKeyProvider);
    }

    [Fact]
    public void AddTenantCacheKeyProvider_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheKeyProvider();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ITenantCacheKeyProvider>();
        var second = provider.GetRequiredService<ITenantCacheKeyProvider>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddTenantCacheKeyProvider_DoesNotReplaceICacheKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheKeyProvider();

        using var provider = services.BuildServiceProvider();

        // Both registrations must coexist independently.
        var baseProvider = provider.GetService<ICacheKeyProvider>();
        var tenantProvider = provider.GetService<ITenantCacheKeyProvider>();

        Assert.NotNull(baseProvider);
        Assert.NotNull(tenantProvider);

        // They are different instances (different registered types).
        // ICacheKeyProvider is still CacheKeyProvider, not TenantCacheKeyProvider.
        Assert.IsType<CacheKeyProvider>(baseProvider);
        Assert.IsType<TenantCacheKeyProvider>(tenantProvider);
    }

    [Fact]
    public void AddTenantCacheKeyProvider_IsIdempotent_CalledTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc")
                .AddTenantCacheKeyProvider()
                .AddTenantCacheKeyProvider(); // second call — must not fail or duplicate

        using var provider = services.BuildServiceProvider();
        var tenantProvider = provider.GetRequiredService<ITenantCacheKeyProvider>();

        Assert.NotNull(tenantProvider);
    }

    [Fact]
    public void AddTenantCacheKeyProvider_UsesServiceNameFromOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "resolved-svc")
                .AddTenantCacheKeyProvider();

        using var provider = services.BuildServiceProvider();
        var tenantKeyProvider = provider.GetRequiredService<ITenantCacheKeyProvider>();

        var key = tenantKeyProvider.BuildTenantKey("t1", "order", "7");
        Assert.Equal("resolved-svc:t1:order:7", key);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static ITenantCacheKeyProvider CreateProvider(string serviceName)
    {
        var options = Options.Create(new CachingCoreOptions { ServiceName = serviceName });
        return new TenantCacheKeyProvider(options);
    }
}
