using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Tests for <see cref="CacheKeyProvider"/> — key format, edge cases,
/// and DI registration via <see cref="CachingServiceCollectionExtensions.AddSharedKernelCaching"/>.
/// </summary>
public sealed class CacheKeyProviderTests
{
    // -------------------------------------------------------------------------
    // Key format — basic cases
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildKey_WithoutExtraSegments_ProducesServiceEntityIdFormat()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42");

        Assert.Equal("order-svc:invoice:42", key);
    }

    [Fact]
    public void BuildKey_WithOneExtraSegment_AppendsWithColon()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42", "v2");

        Assert.Equal("order-svc:invoice:42:v2", key);
    }

    [Fact]
    public void BuildKey_WithMultipleExtraSegments_AppendsAllWithColons()
    {
        var provider = CreateProvider("my-service");

        var key = provider.BuildKey("user-profile", "usr-001", "en-GB", "tenant-99");

        Assert.Equal("my-service:user-profile:usr-001:en-GB:tenant-99", key);
    }

    [Fact]
    public void BuildKey_ServiceNameIsFirstSegment()
    {
        var provider = CreateProvider("catalog");

        var key = provider.BuildKey("product", "sku-x");

        Assert.StartsWith("catalog:", key, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildKey_DoesNotNormaliseCase()
    {
        var provider = CreateProvider("MyService");

        var key = provider.BuildKey("Entity", "ID-001");

        Assert.Equal("MyService:Entity:ID-001", key);
    }

    // -------------------------------------------------------------------------
    // Argument validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_NullOrWhitespaceEntity_ThrowsArgumentException(string entity)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildKey(entity, "123"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_NullOrWhitespaceId_ThrowsArgumentException(string id)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildKey("entity", id));
    }

    // -------------------------------------------------------------------------
    // ServiceName validation — via IValidateOptions resolved from DI
    // -------------------------------------------------------------------------

    [Fact]
    public void ServiceName_NullOrWhitespace_FailsValidation()
    {
        // CachingOptionsValidator is internal; accessible via InternalsVisibleTo.
        var validator = new CachingOptionsValidator();

        var resultNull = validator.Validate(null, new CachingOptions { ServiceName = null! });
        Assert.True(resultNull.Failed);
        Assert.Contains("ServiceName", resultNull.FailureMessage);

        var resultWhitespace = validator.Validate(null, new CachingOptions { ServiceName = "   " });
        Assert.True(resultWhitespace.Failed);
        Assert.Contains("ServiceName", resultWhitespace.FailureMessage);
    }

    [Fact]
    public void ServiceName_Valid_PassesValidation()
    {
        var validator = new CachingOptionsValidator();

        var result = validator.Validate(null, new CachingOptions { ServiceName = "my-service" });

        Assert.True(result.Succeeded);
    }

    // -------------------------------------------------------------------------
    // DI registration sanity
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelCaching_RegistersICacheKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");

        using var provider = services.BuildServiceProvider();
        var keyProvider = provider.GetService<ICacheKeyProvider>();

        Assert.NotNull(keyProvider);
    }

    [Fact]
    public void AddSharedKernelCaching_ICacheKeyProvider_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ICacheKeyProvider>();
        var second = provider.GetRequiredService<ICacheKeyProvider>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddSharedKernelCaching_ICacheKeyProvider_UsesServiceNameFromOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "resolved-svc");

        using var provider = services.BuildServiceProvider();
        var keyProvider = provider.GetRequiredService<ICacheKeyProvider>();

        var key = keyProvider.BuildKey("order", "7");
        Assert.Equal("resolved-svc:order:7", key);
    }

    [Fact]
    public void AddSharedKernelCaching_CustomICacheKeyProvider_OverridesDefault()
    {
        // If a consumer registers their own ICacheKeyProvider *after* AddSharedKernelCaching,
        // the last registration wins (standard DI behavior).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");

        // Override with a custom stub registered after the default.
        services.AddSingleton<ICacheKeyProvider>(new CustomKeyProvider());

        using var provider = services.BuildServiceProvider();
        var keyProvider = provider.GetRequiredService<ICacheKeyProvider>();

        Assert.IsType<CustomKeyProvider>(keyProvider);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ICacheKeyProvider CreateProvider(string serviceName)
    {
        var options = Options.Create(new CachingOptions { ServiceName = serviceName });
        return new CacheKeyProvider(options);
    }

    private sealed class CustomKeyProvider : ICacheKeyProvider
    {
        public string BuildKey(string entity, string id, params string[] extraSegments) =>
            $"custom:{entity}:{id}";
    }
}
