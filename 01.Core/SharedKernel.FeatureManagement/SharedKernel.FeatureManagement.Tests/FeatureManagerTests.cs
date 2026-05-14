using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

public sealed class FeatureManagerTests
{
    // ---- Interface contract via NSubstitute ----

    [Fact]
    public async Task IsEnabledAsync_EnabledFeature_ReturnsTrue()
    {
        var manager = Substitute.For<IFeatureManager>();
        manager.IsEnabledAsync("MyFeature").Returns(new ValueTask<bool>(true));

        var result = await manager.IsEnabledAsync("MyFeature");
        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabledAsync_DisabledFeature_ReturnsFalse()
    {
        var manager = Substitute.For<IFeatureManager>();
        manager.IsEnabledAsync("DisabledFeature").Returns(new ValueTask<bool>(false));

        var result = await manager.IsEnabledAsync("DisabledFeature");
        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabledAsync_ContextVariant_ReturnsExpected()
    {
        var manager = Substitute.For<IFeatureManager>();
        var context = new { TenantId = "tenant-a" };
        manager.IsEnabledAsync("TenantFeature", context).Returns(new ValueTask<bool>(true));

        var result = await manager.IsEnabledAsync("TenantFeature", context);
        Assert.True(result);
    }

    // ---- FeatureDefinition record ----

    [Fact]
    public void FeatureDefinition_DefaultValue_IsFalse()
    {
        var def = new FeatureDefinition("MyFeature");
        Assert.Equal("MyFeature", def.Name);
        Assert.False(def.DefaultValue);
        Assert.Null(def.Description);
    }

    [Fact]
    public void FeatureDefinition_WithExplicitValues_StoresAll()
    {
        var def = new FeatureDefinition("BetaFeature", DefaultValue: true, Description: "Beta rollout");
        Assert.Equal("BetaFeature", def.Name);
        Assert.True(def.DefaultValue);
        Assert.Equal("Beta rollout", def.Description);
    }

    [Fact]
    public void FeatureDefinition_RecordEquality_WorksCorrectly()
    {
        var a = new FeatureDefinition("Feature", true, "desc");
        var b = new FeatureDefinition("Feature", true, "desc");
        Assert.Equal(a, b);
    }

    // ---- DI registration ----

    [Fact]
    public void AddSharedKernelFeatureManagement_RegistersIFeatureManager()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:MyFeature"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetService<IFeatureManager>();
        Assert.NotNull(manager);
    }

    [Fact]
    public async Task AddSharedKernelFeatureManagement_EnabledFlag_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:BetaMode"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IFeatureManager>();
        var enabled = await manager.IsEnabledAsync("BetaMode");
        Assert.True(enabled);
    }

    [Fact]
    public async Task AddSharedKernelFeatureManagement_DisabledFlag_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:BetaMode"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(config);

        var sp = services.BuildServiceProvider();
        var manager = sp.GetRequiredService<IFeatureManager>();
        var enabled = await manager.IsEnabledAsync("BetaMode");
        Assert.False(enabled);
    }
}
