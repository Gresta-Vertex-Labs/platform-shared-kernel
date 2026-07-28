using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.Testing.FeatureManagement;
using Xunit;

namespace SharedKernel.Testing.SelfTests.FeatureManagement;

/// <summary>
/// Proves <see cref="FakeFeatureManager"/> against <c>IFeatureManager</c>'s documented contract,
/// including its variant/allocation surface. No consuming domain can reference
/// <c>16.Testing</c> at all (<c>01.Core</c> sits below it and references nothing), so this
/// self-test is the only behavioral proof — see <c>16.Testing/state-map.md</c> T-59.
/// </summary>
public sealed class FakeFeatureManagerTests
{
    [Fact]
    public async Task IsEnabledAsync_UnconfiguredFeature_DefaultsClosed()
    {
        var manager = new FakeFeatureManager();

        Assert.False(await manager.IsEnabledAsync("unconfigured-feature"));
    }

    [Fact]
    public async Task SetEnabled_True_ThenIsEnabledAsync_RoundTrips()
    {
        var manager = new FakeFeatureManager();

        manager.SetEnabled("my-feature", enabled: true);

        Assert.True(await manager.IsEnabledAsync("my-feature"));
    }

    [Fact]
    public async Task SetEnabled_False_ThenIsEnabledAsync_RoundTrips()
    {
        var manager = new FakeFeatureManager();
        manager.SetEnabled("my-feature", enabled: true);

        manager.SetEnabled("my-feature", enabled: false);

        Assert.False(await manager.IsEnabledAsync("my-feature"));
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_UnconfiguredFeature_DefaultsClosed()
    {
        var manager = new FakeFeatureManager();

        Assert.False(await manager.IsEnabledAsync("unconfigured-feature", context: "tenant-1"));
    }

    [Fact]
    public async Task SetEnabled_ThenIsEnabledAsync_WithContext_RoundTrips()
    {
        var manager = new FakeFeatureManager();

        manager.SetEnabled("my-feature", enabled: true);

        Assert.True(await manager.IsEnabledAsync("my-feature", context: "tenant-1"));
        Assert.True(await manager.IsEnabledAsync("my-feature", context: "tenant-2"));
    }

    [Fact]
    public async Task GetVariantAsync_UnconfiguredFeature_ReturnsUnassigned_NeverThrows()
    {
        var manager = new FakeFeatureManager();

        var variant = await manager.GetVariantAsync("unconfigured-feature");

        Assert.Equal(FeatureVariant.Unassigned, variant);
    }

    [Fact]
    public async Task GetVariantAsync_WithContext_UnconfiguredFeature_ReturnsUnassigned_NeverThrows()
    {
        var manager = new FakeFeatureManager();

        var variant = await manager.GetVariantAsync("unconfigured-feature", context: "tenant-1");

        Assert.Equal(FeatureVariant.Unassigned, variant);
    }

    [Fact]
    public async Task SetVariant_ThenGetVariantAsync_RoundTrips()
    {
        var manager = new FakeFeatureManager();
        var configured = new FeatureVariant("VariantB", "some-config");

        manager.SetVariant("my-feature", configured);

        Assert.Equal(configured, await manager.GetVariantAsync("my-feature"));
    }

    [Fact]
    public async Task SetVariant_ThenGetVariantAsync_WithContext_RoundTrips()
    {
        var manager = new FakeFeatureManager();
        var configured = new FeatureVariant("VariantA");

        manager.SetVariant("my-feature", configured);

        Assert.Equal(configured, await manager.GetVariantAsync("my-feature", context: "tenant-1"));
    }

    [Fact]
    public async Task Reset_ClearsBothTheEnabledAndVariantOverrideMaps()
    {
        var manager = new FakeFeatureManager();
        manager.SetEnabled("my-feature", enabled: true);
        manager.SetVariant("my-feature", new FeatureVariant("VariantB"));

        manager.Reset();

        Assert.False(await manager.IsEnabledAsync("my-feature"));
        Assert.Equal(FeatureVariant.Unassigned, await manager.GetVariantAsync("my-feature"));
    }

    [Fact]
    public async Task DifferentFeatures_AreIndependentlyConfigurable()
    {
        var manager = new FakeFeatureManager();
        manager.SetEnabled("feature-a", enabled: true);
        manager.SetEnabled("feature-b", enabled: false);

        Assert.True(await manager.IsEnabledAsync("feature-a"));
        Assert.False(await manager.IsEnabledAsync("feature-b"));
    }
}
