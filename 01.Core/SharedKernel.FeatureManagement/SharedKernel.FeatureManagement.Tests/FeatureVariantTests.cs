using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>
/// Covers the weighted feature-flag variant/experimentation surface added in P-298/WO-049:
/// <see cref="FeatureVariant"/>, <see cref="FeatureVariantDefinition"/>, and
/// <see cref="IFeatureManager"/>'s <c>GetVariantAsync</c> members.
/// </summary>
public sealed class FeatureVariantTests
{
    // ---- FeatureVariant record ----

    [Fact]
    public void FeatureVariant_WithNameOnly_ConfigurationIsNull()
    {
        var variant = new FeatureVariant("ControlGroup");

        Assert.Equal("ControlGroup", variant.Name);
        Assert.Null(variant.Configuration);
    }

    [Fact]
    public void FeatureVariant_WithConfiguration_StoresBoth()
    {
        var variant = new FeatureVariant("VariantB", "some-config");

        Assert.Equal("VariantB", variant.Name);
        Assert.Equal("some-config", variant.Configuration);
    }

    [Fact]
    public void FeatureVariant_RecordEquality_WorksCorrectly()
    {
        var a = new FeatureVariant("VariantB", "config");
        var b = new FeatureVariant("VariantB", "config");

        Assert.Equal(a, b);
    }

    [Fact]
    public void FeatureVariant_Unassigned_IsDocumentedDeterministicSentinel()
    {
        Assert.Equal("Unassigned", FeatureVariant.Unassigned.Name);
        Assert.Null(FeatureVariant.Unassigned.Configuration);
        Assert.Same(FeatureVariant.Unassigned, FeatureVariant.Unassigned);
    }

    // ---- FeatureVariantDefinition record ----

    [Fact]
    public void FeatureVariantDefinition_StoresAllValues()
    {
        var definition = new FeatureVariantDefinition("VariantB", 50, "config-payload");

        Assert.Equal("VariantB", definition.Name);
        Assert.Equal(50, definition.Weight);
        Assert.Equal("config-payload", definition.Configuration);
    }

    [Fact]
    public void FeatureVariantDefinition_ConfigurationDefaultsToNull()
    {
        var definition = new FeatureVariantDefinition("ControlGroup", 50);

        Assert.Null(definition.Configuration);
    }

    [Fact]
    public void FeatureVariantDefinition_RecordEquality_WorksCorrectly()
    {
        var a = new FeatureVariantDefinition("VariantB", 50, "config");
        var b = new FeatureVariantDefinition("VariantB", 50, "config");

        Assert.Equal(a, b);
    }

    // ---- Interface contract via NSubstitute ----

    [Fact]
    public async Task GetVariantAsync_ReturnsSubstitutedVariant()
    {
        var manager = Substitute.For<IFeatureManager>();
        manager.GetVariantAsync("Beta").Returns(new ValueTask<FeatureVariant>(new FeatureVariant("VariantB")));

        var result = await manager.GetVariantAsync("Beta");

        Assert.Equal("VariantB", result.Name);
    }

    [Fact]
    public async Task GetVariantAsync_WithContext_ReturnsSubstitutedVariant()
    {
        var manager = Substitute.For<IFeatureManager>();
        const string context = "tenant-a";
        manager.GetVariantAsync("Beta", context)
            .Returns(new ValueTask<FeatureVariant>(new FeatureVariant("VariantB", "cfg")));

        var result = await manager.GetVariantAsync("Beta", context);

        Assert.Equal("VariantB", result.Name);
        Assert.Equal("cfg", result.Configuration);
    }

    // ---- Public surface never leaks a Microsoft.FeatureManagement type ----

    [Fact]
    public void IFeatureManager_PublicSurface_NeverExposesMicrosoftFeatureManagementType()
    {
        foreach (var method in typeof(IFeatureManager).GetMethods())
        {
            AssertNoMicrosoftType(method.ReturnType, method.Name);

            foreach (var parameter in method.GetParameters())
            {
                AssertNoMicrosoftType(parameter.ParameterType, method.Name);
            }
        }

        static void AssertNoMicrosoftType(Type type, string memberName)
        {
            IEnumerable<Type> candidates = type.IsGenericType
                ? new[] { type.GetGenericTypeDefinition() }.Concat(type.GetGenericArguments())
                : [type];

            foreach (var candidate in candidates)
            {
                Assert.False(
                    candidate.Namespace?.StartsWith("Microsoft.FeatureManagement", StringComparison.Ordinal) == true,
                    $"{memberName} exposes Microsoft.FeatureManagement type {candidate.FullName}");
            }
        }
    }

    // ---- Deterministic variant assignment + unconfigured fallback (real Microsoft.FeatureManagement pipeline) ----

    private static IConfiguration BuildVariantConfiguration()
    {
        // Microsoft Feature Management schema (feature_management:feature_flags array) — the only
        // schema Microsoft.FeatureManagement resolves variants/allocation from (P-298/WO-049,
        // confirmed empirically). Expressed as colon-path in-memory keys rather than JSON so this
        // test project takes on no additional configuration-provider package dependency.
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["feature_management:feature_flags:0:id"] = "Beta",
                ["feature_management:feature_flags:0:enabled"] = "true",
                ["feature_management:feature_flags:0:variants:0:name"] = "ControlGroup",
                ["feature_management:feature_flags:0:variants:0:configuration_value"] = "control-config",
                ["feature_management:feature_flags:0:variants:1:name"] = "VariantB",
                ["feature_management:feature_flags:0:variants:1:configuration_value"] = "variant-b-config",
                ["feature_management:feature_flags:0:allocation:default_when_enabled"] = "ControlGroup",
                ["feature_management:feature_flags:0:allocation:user:0:variant"] = "VariantB",
                ["feature_management:feature_flags:0:allocation:user:0:users:0"] = "alice-tenant",

                // Legacy .NET-schema plain boolean flag, coexisting in the same root configuration —
                // proves AddSharedKernelFeatureManagement's schema fix (root config, not a pre-scoped
                // "FeatureManagement" section) does not regress plain boolean flags.
                ["FeatureManagement:PlainBoolFeature"] = "true",
            })
            .Build();
    }

    private static IFeatureManager BuildRealFeatureManager(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(configuration);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IFeatureManager>();
    }

    [Fact]
    public async Task GetVariantAsync_WithContext_SameContext_IsDeterministicAcrossRepeatedCalls()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var first = await manager.GetVariantAsync("Beta", "alice-tenant");
        for (var i = 0; i < 5; i++)
        {
            var repeat = await manager.GetVariantAsync("Beta", "alice-tenant");
            Assert.Equal(first.Name, repeat.Name);
            Assert.Equal(first.Configuration, repeat.Configuration);
        }

        Assert.Equal("VariantB", first.Name);
        Assert.Equal("variant-b-config", first.Configuration);
    }

    [Fact]
    public async Task GetVariantAsync_WithContext_UntargetedContext_ResolvesDefaultAllocation()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var result = await manager.GetVariantAsync("Beta", "someone-not-targeted");

        Assert.Equal("ControlGroup", result.Name);
        Assert.Equal("control-config", result.Configuration);
    }

    [Fact]
    public async Task GetVariantAsync_NoContext_ResolvesDefaultWhenEnabledAllocation()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var result = await manager.GetVariantAsync("Beta");

        Assert.Equal("ControlGroup", result.Name);
    }

    [Fact]
    public async Task GetVariantAsync_UnconfiguredFeature_ReturnsUnassignedSentinel_NeverThrows()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var result = await manager.GetVariantAsync("ThisFeatureDoesNotExist");

        Assert.Equal(FeatureVariant.Unassigned, result);
    }

    [Fact]
    public async Task GetVariantAsync_WithContext_UnconfiguredFeature_ReturnsUnassignedSentinel_NeverThrows()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var result = await manager.GetVariantAsync("ThisFeatureDoesNotExist", "any-context");

        Assert.Equal(FeatureVariant.Unassigned, result);
    }

    [Fact]
    public async Task GetVariantAsync_WithNonStringContext_UsesToStringForDeterministicAssignment()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        // A context type whose ToString() reproduces the same targeted identity string used in
        // configuration ("alice-tenant") must resolve identically to passing that string directly.
        var first = await manager.GetVariantAsync("Beta", new TenantContext("alice-tenant"));
        var second = await manager.GetVariantAsync("Beta", new TenantContext("alice-tenant"));
        var viaRawString = await manager.GetVariantAsync("Beta", "alice-tenant");

        Assert.Equal("VariantB", first.Name);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(viaRawString.Name, first.Name);
    }

    private sealed record TenantContext(string TenantId)
    {
        public override string ToString() => TenantId;
    }

    // ---- Regression: existing boolean IsEnabledAsync surface/behavior is unchanged (T-42) ----

    [Fact]
    public async Task IsEnabledAsync_StillResolvesPlainBooleanFlags_AfterVariantWiringChange()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var enabled = await manager.IsEnabledAsync("PlainBoolFeature");

        Assert.True(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_UnconfiguredFlag_StillReturnsFalse_AfterVariantWiringChange()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var enabled = await manager.IsEnabledAsync("NoSuchFlag");

        Assert.False(enabled);
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_StillWorks_AfterVariantWiringChange()
    {
        var manager = BuildRealFeatureManager(BuildVariantConfiguration());

        var enabled = await manager.IsEnabledAsync("Beta", "alice-tenant");

        Assert.True(enabled);
    }

    // ---- DI registration sanity ----

    [Fact]
    public void AddSharedKernelFeatureManagement_RootConfiguration_ResolvesIFeatureManagerWithVariantSupport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelFeatureManagement(BuildVariantConfiguration());

        var provider = services.BuildServiceProvider();
        var manager = provider.GetService<IFeatureManager>();

        Assert.NotNull(manager);
    }
}
