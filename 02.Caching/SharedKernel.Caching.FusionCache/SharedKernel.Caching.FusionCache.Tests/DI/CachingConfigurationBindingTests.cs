using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Serialization;
using Xunit;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Tests.DI;

/// <summary>
/// <c>AddSharedKernelCaching(IConfiguration, configure)</c>: binds <c>SharedKernel:Caching</c>, applies the
/// delegate after binding, validates at options resolution and at startup, and the bound values reach the
/// memory cache, FusionCache's default entry options and the serializer.
/// </summary>
public sealed class CachingConfigurationBindingTests
{
    [Fact]
    public void SectionName_IsSharedKernelCaching() =>
        Assert.Equal("SharedKernel:Caching", CachingOptions.SectionName);

    [Fact]
    public void ConfigurationOverload_BindsEverySetting()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:L1SizeLimit"] = "250",
            ["SharedKernel:Caching:WaitForWarmup"] = "true",
            ["SharedKernel:Caching:DistributedCacheSoftTimeout"] = "00:00:00.150",
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:02",
            ["SharedKernel:Caching:FailSafeThrottleDuration"] = "00:00:45",
        });

        var options = provider.GetRequiredService<IOptions<CachingOptions>>().Value;

        Assert.Equal("orders", options.ServiceName);
        Assert.Equal(250, options.L1SizeLimit);
        Assert.True(options.WaitForWarmup);
        Assert.Equal(TimeSpan.FromMilliseconds(150), options.DistributedCacheSoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.DistributedCacheHardTimeout);
        Assert.Equal(TimeSpan.FromSeconds(45), options.FailSafeThrottleDuration);
        Assert.Null(options.SerializerContext);
    }

    [Fact]
    public void ConfigurationOverload_BoundServiceName_ReachesTheKeyProviders()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
        });

        Assert.Equal("orders:invoice:42", provider.GetRequiredService<ICacheKeyProvider>().BuildKey("invoice", "42"));
    }

    [Fact]
    public void ConfigurationOverload_OtherSections_AreIgnored()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernelCaching:ServiceName"] = "old-section",
            ["SharedKernel:Caching:ServiceName"] = "new-section",
        });

        Assert.Equal("new-section", provider.GetRequiredService<IOptions<CachingOptions>>().Value.ServiceName);
    }

    [Fact]
    public void ConfigurationOverload_BoundTimeouts_ReachFusionCacheDefaultEntryOptions()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:DistributedCacheSoftTimeout"] = "00:00:00.150",
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:02",
            ["SharedKernel:Caching:FailSafeThrottleDuration"] = "00:00:45",
        });

        var defaults = provider.GetRequiredService<IFusionCache>().DefaultEntryOptions;

        Assert.Equal(TimeSpan.FromMilliseconds(150), defaults.DistributedCacheSoftTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), defaults.DistributedCacheHardTimeout);
        Assert.Equal(TimeSpan.FromSeconds(45), defaults.FailSafeThrottleDuration);
        Assert.Equal(1, defaults.Size);
    }

    [Fact]
    public void ConfigurationOverload_UnsetTimeouts_LeaveFusionCacheDefaults()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
        });

        var fusionDefaults = new FusionCacheEntryOptions();
        var defaults = provider.GetRequiredService<IFusionCache>().DefaultEntryOptions;

        Assert.Equal(fusionDefaults.DistributedCacheSoftTimeout, defaults.DistributedCacheSoftTimeout);
        Assert.Equal(fusionDefaults.DistributedCacheHardTimeout, defaults.DistributedCacheHardTimeout);
        Assert.Equal(fusionDefaults.FailSafeThrottleDuration, defaults.FailSafeThrottleDuration);
    }

    [Fact]
    public async Task ConfigurationOverload_BoundL1SizeLimit_CapsTheMemoryCache()
    {
        const int limit = 5;
        await using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:L1SizeLimit"] = limit.ToString(),
        });
        var cache = provider.GetRequiredService<ICacheService>();
        var policy = CachePolicy.For(TimeSpan.FromMinutes(10));

        for (var i = 0; i < 50; i++)
            await cache.SetAsync($"orders:item:{i}", i, policy);

        await Task.Delay(100); // compaction runs in the background

        var live = 0;
        for (var i = 0; i < 50; i++)
        {
            if ((await cache.TryGetAsync<int>($"orders:item:{i}")).IsHit)
                live++;
        }

        // Without the bound limit all 50 entries would still be live.
        Assert.InRange(live, 1, limit);
    }

    [Fact]
    public void ConfigurationOverload_BoundL1SizeLimit_IsTheSizeLimitOfTheResolvedMemoryCache()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:L1SizeLimit"] = "3",
        });
        var cache = provider.GetRequiredService<ICacheService>();

        // A memory cache with SizeLimit rejects entries without a Size; every write therefore must carry Size = 1,
        // and a fourth distinct write cannot fit.
        var fusion = provider.GetRequiredService<IFusionCache>();
        for (var i = 0; i < 3; i++)
            fusion.Set($"orders:raw:{i}", i);

        fusion.Set("orders:raw:overflow", 99);

        Assert.False(fusion.TryGet<int>("orders:raw:overflow").HasValue);
        Assert.NotNull(cache);
    }

    [Fact]
    public void ConfigureDelegate_RunsAfterBinding_AndOverridesBoundValues()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["SharedKernel:Caching:ServiceName"] = "from-config",
                ["SharedKernel:Caching:L1SizeLimit"] = "7",
                ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:02",
            },
            o =>
            {
                o.L1SizeLimit = 99;
                o.DistributedCacheHardTimeout = TimeSpan.FromSeconds(5);
            });

        var options = provider.GetRequiredService<IOptions<CachingOptions>>().Value;

        Assert.Equal("from-config", options.ServiceName); // untouched by the delegate
        Assert.Equal(99, options.L1SizeLimit);
        Assert.Equal(TimeSpan.FromSeconds(5), options.DistributedCacheHardTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<IFusionCache>().DefaultEntryOptions.DistributedCacheHardTimeout);
    }

    [Fact]
    public void ConfigureDelegate_CanSupplyAValueMissingFromConfiguration()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>(), o => o.ServiceName = "from-code");

        Assert.Equal("from-code", provider.GetRequiredService<IOptions<CachingOptions>>().Value.ServiceName);
    }

    [Fact]
    public void ConfigurationOverload_NullArguments_Throw()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => services.AddSharedKernelCaching((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddSharedKernelCaching(configuration));
    }

    // -------------------------------------------------------------------------
    // SerializerContext set through the delegate
    // -------------------------------------------------------------------------

    [Fact]
    public void ConfigureDelegate_SerializerContext_IsUsedByTheRegisteredSerializer()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?> { ["SharedKernel:Caching:ServiceName"] = "orders" },
            o => o.SerializerContext = WireTestSerializerContext.Default);

        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();
        var dto = new WireTestDto("hello", 3);

        Assert.Equal(dto, serializer.Deserialize<WireTestDto>(serializer.Serialize(dto)));

        // The context-only resolver has no metadata for an unlisted type, so reflection is not in use.
        Assert.ThrowsAny<Exception>(() => serializer.Serialize(new UnlistedDto("x")));

        var jsonOptions = provider.GetRequiredService<CacheSerializationOptions>().Value;
        Assert.NotNull(jsonOptions.TypeInfoResolver);
        Assert.NotNull(jsonOptions.TypeInfoResolver.GetTypeInfo(typeof(WireTestDto), jsonOptions));
        Assert.NotNull(jsonOptions.TypeInfoResolver.GetTypeInfo(typeof(byte[]), jsonOptions));
    }

    [Fact]
    public void WithoutSerializerContext_JsonUsesGeneralDefaults()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["SharedKernel:Caching:ServiceName"] = "orders" });

        var serializer = provider.GetRequiredService<IFusionCacheSerializer>();
        var json = System.Text.Encoding.UTF8.GetString(serializer.Serialize(new UnlistedDto("x")));

        // General defaults keep property names as declared (Web defaults would camel-case them).
        Assert.Equal("{\"Value\":\"x\"}", json);
        Assert.Equal(json, JsonSerializer.Serialize(new UnlistedDto("x"), provider.GetRequiredService<CacheSerializationOptions>().Value));
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("Orders")]
    [InlineData("orders service")]
    [InlineData("orders:api")]
    public void InvalidServiceName_FailsAtOptionsResolution_AndStartupValidation(string? serviceName)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["SharedKernel:Caching:ServiceName"] = serviceName });

        var resolution = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CachingOptions>>().Value);
        Assert.Contains("ServiceName", resolution.Message);

        var startup = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("ServiceName", startup.Message);
    }

    [Theory]
    [InlineData("DistributedCacheSoftTimeout", "00:00:00")]
    [InlineData("DistributedCacheSoftTimeout", "-00:00:01")]
    [InlineData("DistributedCacheHardTimeout", "00:00:00")]
    [InlineData("DistributedCacheHardTimeout", "-00:00:01")]
    [InlineData("FailSafeThrottleDuration", "00:00:00")]
    [InlineData("FailSafeThrottleDuration", "-00:00:05")]
    public void NonPositiveDuration_FailsValidation(string property, string value)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:" + property] = value,
        });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains($"CachingOptions.{property} must be positive.", ex.Failures);
    }

    [Theory]
    [InlineData("00:00:01", "00:00:01")]
    [InlineData("00:00:02", "00:00:01")]
    public void SoftTimeoutNotShorterThanHardTimeout_FailsValidation(string soft, string hard)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:DistributedCacheSoftTimeout"] = soft,
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = hard,
        });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CachingOptions>>().Value);

        Assert.Contains("CachingOptions.DistributedCacheSoftTimeout must be shorter than DistributedCacheHardTimeout.", ex.Failures);
    }

    [Fact]
    public void SoftTimeoutShorterThanHardTimeout_PassesValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:DistributedCacheSoftTimeout"] = "00:00:00.999",
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:01",
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void L1SizeLimitBelowOne_FailsDataAnnotationValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:L1SizeLimit"] = "0",
        });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CachingOptions>>().Value);

        Assert.Contains("L1SizeLimit", ex.Message);
    }

    [Fact]
    public void DelegateOverload_InvalidTimeouts_FailValidationToo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "orders";
            o.DistributedCacheSoftTimeout = TimeSpan.FromSeconds(3);
            o.DistributedCacheHardTimeout = TimeSpan.FromSeconds(3);
            o.FailSafeThrottleDuration = TimeSpan.Zero;
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("CachingOptions.FailSafeThrottleDuration must be positive.", ex.Failures);
        Assert.Contains("CachingOptions.DistributedCacheSoftTimeout must be shorter than DistributedCacheHardTimeout.", ex.Failures);
    }

    [Fact]
    public void InvalidOptions_ResolvingTheCache_Fails()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:00",
        });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IFusionCache>());
    }

    [Fact]
    public async Task InvalidConfiguration_HostStartFails()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:FailSafeThrottleDuration"] = "-00:00:01",
        });
        builder.Services.AddSharedKernelCaching(builder.Configuration);

        using var host = builder.Build();

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Contains("CachingOptions.FailSafeThrottleDuration must be positive.", ex.Failures);
    }

    [Fact]
    public async Task ValidConfiguration_HostStarts()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:ServiceName"] = "orders",
            ["SharedKernel:Caching:DistributedCacheHardTimeout"] = "00:00:01",
        });
        builder.Services.AddSharedKernelCaching(builder.Configuration);

        using var host = builder.Build();
        await host.StartAsync();

        Assert.Equal(42, await host.Services.GetRequiredService<ICacheService>()
            .GetOrSetAsync("orders:x:1", _ => ValueTask.FromResult(42), CachePolicy.Default));

        await host.StopAsync();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, Action<CachingOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(configuration, configure);
        return services.BuildServiceProvider();
    }

    private sealed record UnlistedDto(string Value);
}
