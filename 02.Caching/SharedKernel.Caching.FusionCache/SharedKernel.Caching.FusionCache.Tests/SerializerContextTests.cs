using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Serialization;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

// ---------------------------------------------------------------------------
// Minimal source-generated context used in serializer wiring tests
// ---------------------------------------------------------------------------

/// <summary>Minimal DTO used to verify STJ source-gen context wiring.</summary>
internal sealed record WireTestDto(string Name, int Count);

[JsonSerializable(typeof(WireTestDto))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class WireTestSerializerContext : JsonSerializerContext { }

/// <summary>
/// Unit tests verifying that <see cref="CachingOptions.SerializerContext"/> is correctly
/// threaded into FusionCache's System.Text.Json serializer registration.
/// </summary>
public sealed class SerializerContextTests
{
    // -------------------------------------------------------------------------
    // When SerializerContext is set — verify the combined resolver is in use
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelCaching_WithSerializerContext_CachingOptionsHoldsContext()
    {
        // Verify that the option is captured and accessible after configuration.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "test-svc";
            o.SerializerContext = WireTestSerializerContext.Default;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CachingOptions>>().Value;

        Assert.Same(WireTestSerializerContext.Default, options.SerializerContext);
    }

    [Fact]
    public void AddSharedKernelCaching_WithSerializerContext_CombinedResolverContainsApplicationType()
    {
        // AddSharedKernelCaching combines the service's context with EncryptedCacheEntryJsonContext,
        // so the resolver must resolve both the application's type and the encrypted-entry type.
        var combined = JsonTypeInfoResolver.Combine(
            WireTestSerializerContext.Default,
            EncryptedCacheEntryJsonContext.Default);

        var appTypeInfo = combined.GetTypeInfo(typeof(WireTestDto), new JsonSerializerOptions());
        var infraTypeInfo = combined.GetTypeInfo(typeof(byte[]), new JsonSerializerOptions());

        Assert.NotNull(appTypeInfo);
        Assert.NotNull(infraTypeInfo);
    }

    [Fact]
    public void AddSharedKernelCaching_WithSerializerContext_RegisteredOptionsRoundTripBothTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "test-svc";
            o.SerializerContext = WireTestSerializerContext.Default;
        });

        using var provider = services.BuildServiceProvider();
        var jsonOptions = provider.GetRequiredService<CacheSerializationOptions>().Value;

        var dto = new WireTestDto("hello", 99);
        var dtoJson = JsonSerializer.Serialize(dto, jsonOptions);
        Assert.Equal(dto, JsonSerializer.Deserialize<WireTestDto>(dtoJson, jsonOptions));

        byte[] entry = [1, 2, 3, 255];
        var entryJson = JsonSerializer.Serialize(entry, jsonOptions);
        Assert.Equal(entry, JsonSerializer.Deserialize<byte[]>(entryJson, jsonOptions));
    }

    // -------------------------------------------------------------------------
    // When SerializerContext is NOT set
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelCaching_WithoutSerializerContext_SerializerContextIsNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "test-svc");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CachingOptions>>().Value;

        Assert.Null(options.SerializerContext);
    }

    // -------------------------------------------------------------------------
    // CachingOptions property contract
    // -------------------------------------------------------------------------

    [Fact]
    public void CachingOptions_SerializerContext_DefaultsToNull()
    {
        var options = new CachingOptions();
        Assert.Null(options.SerializerContext);
    }

    [Fact]
    public void CachingOptions_SerializerContext_CanBeSet()
    {
        var options = new CachingOptions
        {
            SerializerContext = WireTestSerializerContext.Default
        };
        Assert.Same(WireTestSerializerContext.Default, options.SerializerContext);
    }

    // -------------------------------------------------------------------------
    // DI sanity — AddSharedKernelCaching still registers ICacheService
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelCaching_WithSerializerContext_RegistersICacheService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "test-svc";
            o.SerializerContext = WireTestSerializerContext.Default;
        });

        using var provider = services.BuildServiceProvider();
        var cacheService = provider.GetService<ICacheService>();

        Assert.NotNull(cacheService);
    }
}
