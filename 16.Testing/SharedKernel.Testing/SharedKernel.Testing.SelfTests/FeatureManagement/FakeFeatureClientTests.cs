using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using SharedKernel.FeatureManagement;
using SharedKernel.Testing.FeatureManagement;
using Xunit;

namespace SharedKernel.Testing.SelfTests.FeatureManagement;

/// <summary>Proves <see cref="FakeFeatureClient"/> honours the real client's contract.</summary>
public sealed partial class FakeFeatureClientTests
{
    private static readonly FeatureFlag<bool> NewCheckout = FeatureFlag.Boolean("NewCheckout");
    private static readonly FeatureFlag<string> Theme = FeatureFlag.String("Theme", "classic");
    private static readonly FeatureFlag<int> PageSize = FeatureFlag.Integer("PageSize", 20);
    private static readonly FeatureFlag<double> Discount = FeatureFlag.Double("Discount", 0);
    private static readonly FeatureFlag<Banner?> Promo = FeatureFlag.Object<Banner?>("Promo", null, FakeJson.Default.Banner);

    public sealed record Banner(string Text, int Priority);

    [JsonSerializable(typeof(Banner))]
    private sealed partial class FakeJson : JsonSerializerContext;

    [Fact]
    public async Task AnUnsetFlag_ReturnsItsDefault_WithFlagNotFound()
    {
        var fake = new FakeFeatureClient();

        FlagEvaluationDetails<string> details = await fake.GetDetailsAsync(Theme);

        Assert.Equal("classic", details.Value);
        Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);
    }

    [Fact]
    public async Task SetValues_AreReturned_ForEveryType()
    {
        var fake = new FakeFeatureClient()
            .SetEnabled(NewCheckout)
            .Set(Theme, "dark")
            .Set(PageSize, 50)
            .Set(Discount, 0.2)
            .SetObject(Promo, new Banner("Spring sale", 2), FakeJson.Default.Banner);

        Assert.True(await fake.IsEnabledAsync(NewCheckout));
        Assert.Equal("dark", await fake.GetValueAsync(Theme));
        Assert.Equal(50, await fake.GetValueAsync(PageSize));
        Assert.Equal(0.2, await fake.GetValueAsync(Discount));
        Assert.Equal(new Banner("Spring sale", 2), await fake.GetValueAsync(Promo));
    }

    [Fact]
    public async Task ARule_SeesTheExplicitAndTheClientContext()
    {
        var fake = new FakeFeatureClient()
            .Set(NewCheckout, ctx => ctx.GetValue(FeatureContextKeys.TenantId)?.AsString == "acme");

        Assert.True(await fake.IsEnabledAsync(NewCheckout, FeatureTargetingContext.ForTenant("acme").ToEvaluationContext()));
        Assert.False(await fake.IsEnabledAsync(NewCheckout, FeatureTargetingContext.ForTenant("other").ToEvaluationContext()));

        fake.SetContext(FeatureTargetingContext.ForTenant("acme").ToEvaluationContext());
        Assert.True(await fake.IsEnabledAsync(NewCheckout));
    }

    [Fact]
    public void AnObjectFlag_MustBeSetWithItsTypeInfo()
    {
        var fake = new FakeFeatureClient();

        Assert.Throws<ArgumentException>(() => fake.Set(Promo, new Banner("x", 1)));
    }

    [Fact]
    public async Task Evaluations_AndTrackedEvents_AreRecorded()
    {
        var fake = new FakeFeatureClient().SetEnabled(NewCheckout);

        await fake.IsEnabledAsync(NewCheckout);
        await fake.IsEnabledAsync(NewCheckout);
        fake.Track("checkout-completed");

        Assert.True(fake.WasEvaluated(NewCheckout));
        Assert.False(fake.WasEvaluated(Theme));
        Assert.Equal(["NewCheckout", "NewCheckout"], fake.EvaluatedFlags);
        Assert.Equal(["checkout-completed"], fake.TrackedEvents);
    }

    [Fact]
    public async Task Reset_ForgetsEverything()
    {
        var fake = new FakeFeatureClient().SetEnabled(NewCheckout);
        await fake.IsEnabledAsync(NewCheckout);

        fake.Reset();

        Assert.False(await fake.IsEnabledAsync(NewCheckout));
        Assert.Equal(["NewCheckout"], fake.EvaluatedFlags);
    }

    [Fact]
    public void AddFakeFeatureFlags_RegistersOneInstance_AsItselfAndAsIFeatureClient()
    {
        var services = new ServiceCollection();
        services.AddFakeFeatureFlags(f => f.SetEnabled(NewCheckout));
        using ServiceProvider provider = services.BuildServiceProvider();

        FakeFeatureClient fake = provider.GetRequiredService<FakeFeatureClient>();
        Assert.Same(fake, provider.GetRequiredService<IFeatureClient>());
        Assert.Same(fake, provider.CreateScope().ServiceProvider.GetRequiredService<IFeatureClient>());
    }

    // README recipe 7 ("Test code that reads flags"), as written.
    private static class Flags
    {
        public static readonly FeatureFlag<bool> NewCheckout = FeatureFlag.Boolean("NewCheckout");
        public static readonly FeatureFlag<string> CheckoutTheme = FeatureFlag.String("CheckoutTheme", defaultValue: "classic");
        public static readonly FeatureFlag<bool> Exports = FeatureFlag.Boolean("Exports");
    }

    private sealed class CheckoutEndpoint(IFeatureClient flags)
    {
        public async Task<string> GetLayoutAsync(CancellationToken ct) =>
            await flags.IsEnabledAsync(Flags.NewCheckout, ct)
                ? $"new-checkout/{await flags.GetValueAsync(Flags.CheckoutTheme, ct)}"
                : "legacy-checkout";
    }

    [Fact]
    public async Task ReadmeRecipe7_TestCodeThatReadsFlags()
    {
        var flags = new FakeFeatureClient()
            .SetEnabled(Flags.NewCheckout)
            .Set(Flags.CheckoutTheme, "dark")
            .Set(Flags.Exports, ctx => ctx.GetValue(FeatureContextKeys.TenantId)?.AsString == "acme");

        var endpoint = new CheckoutEndpoint(flags);
        Assert.Equal("new-checkout/dark", await endpoint.GetLayoutAsync(CancellationToken.None));
        Assert.True(flags.WasEvaluated(Flags.NewCheckout));
        Assert.True(await flags.IsEnabledAsync(Flags.Exports, FeatureTargetingContext.ForTenant("acme").ToEvaluationContext()));
    }

    [Fact]
    public void AddFakeFeatureFlags_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddFakeFeatureFlags());
}
