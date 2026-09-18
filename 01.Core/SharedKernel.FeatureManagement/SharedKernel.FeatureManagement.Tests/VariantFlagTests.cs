using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>String, number and object flags read from the assigned variant's configuration_value.</summary>
public sealed class VariantFlagTests
{
    internal const string Configuration = """
        {
          "feature_management": {
            "feature_flags": [
              {
                "id": "Theme",
                "enabled": true,
                "variants": [
                  { "name": "Classic", "configuration_value": "classic" },
                  { "name": "Dark", "configuration_value": "dark" }
                ],
                "allocation": {
                  "default_when_enabled": "Classic",
                  "user": [ { "variant": "Dark", "users": [ "alice" ] } ],
                  "group": [ { "variant": "Dark", "groups": [ "tenant-acme" ] } ]
                }
              },
              {
                "id": "PageSize",
                "enabled": true,
                "variants": [
                  { "name": "Small", "configuration_value": "20" },
                  { "name": "Large", "configuration_value": "100" }
                ],
                "allocation": {
                  "default_when_enabled": "Small",
                  "percentile": [
                    { "variant": "Small", "from": 0, "to": 50 },
                    { "variant": "Large", "from": 50, "to": 100 }
                  ]
                }
              },
              {
                "id": "Discount",
                "enabled": true,
                "variants": [ { "name": "Standard", "configuration_value": "0.15" } ],
                "allocation": { "default_when_enabled": "Standard" }
              },
              {
                "id": "Checkout",
                "enabled": true,
                "variants": [
                  {
                    "name": "V2",
                    "configuration_value": {
                      "steps": 3,
                      "expressPay": true,
                      "title": "Fast checkout",
                      "providers": [ "card", "iban" ],
                      "code": "007"
                    }
                  }
                ],
                "allocation": { "default_when_enabled": "V2" }
              },
              {
                "id": "BrokenNumber",
                "enabled": true,
                "variants": [ { "name": "Bad", "configuration_value": "lots" } ],
                "allocation": { "default_when_enabled": "Bad" }
              },
              {
                "id": "SwitchedOff",
                "enabled": false,
                "variants": [ { "name": "Fallback", "configuration_value": "fallback" } ],
                "allocation": { "default_when_disabled": "Fallback" }
              },
              {
                "id": "NoAllocation",
                "enabled": true,
                "variants": [ { "name": "A", "configuration_value": "a" } ]
              }
            ]
          }
        }
        """;

    private static readonly FeatureFlag<string> Theme = FeatureFlag.String("Theme", "fallback");
    private static readonly FeatureFlag<int> PageSize = FeatureFlag.Integer("PageSize", 10);
    private static readonly FeatureFlag<double> Discount = FeatureFlag.Double("Discount", 0);
    private static readonly FeatureFlag<CheckoutSettings?> Checkout =
        FeatureFlag.Object<CheckoutSettings?>("Checkout", null, TestJsonContext.Default.CheckoutSettings);

    [Fact]
    public async Task StringFlag_ReturnsTheDefaultAllocation_WithVariantAndReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<string> details = await provider.NewScopeClient().GetDetailsAsync(Theme);

        Assert.Equal("classic", details.Value);
        Assert.Equal("Classic", details.Variant);
        Assert.Equal(Reason.TargetingMatch, details.Reason);
    }

    [Fact]
    public async Task StringFlag_AllocatesTheTargetedUserAndTenant()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));
        IFeatureClient client = provider.NewScopeClient();

        Assert.Equal("dark", await client.GetValueAsync(Theme, new FeatureTargetingContext("alice").ToEvaluationContext()));
        Assert.Equal("dark", await client.GetValueAsync(Theme, FeatureTargetingContext.ForTenant("tenant-acme").ToEvaluationContext()));
        Assert.Equal("classic", await client.GetValueAsync(Theme, new FeatureTargetingContext("bob").ToEvaluationContext()));
    }

    [Fact]
    public async Task IntegerFlag_SplitsByPercentile_WithSplitReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        var seen = new HashSet<int>();
        for (int i = 0; i < 100; i++)
        {
            FlagEvaluationDetails<int> details = await provider.NewScopeClient()
                .GetDetailsAsync(PageSize, new FeatureTargetingContext($"user-{i}").ToEvaluationContext());

            Assert.Equal(Reason.Split, details.Reason);
            seen.Add(details.Value);
        }

        Assert.Equal([20, 100], seen.Order());
    }

    [Fact]
    public async Task DoubleFlag_ReadsInvariantCulture_WithStaticReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<double> details = await provider.NewScopeClient().GetDetailsAsync(Discount);

        Assert.Equal(0.15, details.Value);
        Assert.Equal(Reason.Static, details.Reason);
    }

    [Fact]
    public async Task ObjectFlag_ReadsEachValueAsItsPropertyType()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<CheckoutSettings?> details = await provider.NewScopeClient().GetDetailsAsync(Checkout);

        Assert.Equal(ErrorType.None, details.ErrorType);
        Assert.Equal("V2", details.Variant);
        CheckoutSettings settings = Assert.IsType<CheckoutSettings>(details.Value);
        Assert.Equal(3, settings.Steps);                    // "3" in configuration, an int property
        Assert.True(settings.ExpressPay);                   // "True" in configuration, a bool property
        Assert.Equal("Fast checkout", settings.Title);
        Assert.Equal(["card", "iban"], settings.Providers); // a JSON array, a list property
        Assert.Equal("007", settings.Code);                 // looks like a number, stays text for a string property
    }

    [Fact]
    public async Task ObjectFlag_OverAScalarVariant_ReturnsTheDefault_WithParseError()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));
        var wrongShape = FeatureFlag.Object<CheckoutSettings?>("Theme", null, TestJsonContext.Default.CheckoutSettings);

        FlagEvaluationDetails<CheckoutSettings?> details = await provider.NewScopeClient().GetDetailsAsync(wrongShape);

        Assert.Null(details.Value);
        Assert.Equal(ErrorType.ParseError, details.ErrorType);
        Assert.Equal("Classic", details.Variant);
    }

    [Fact]
    public async Task NumberFlag_OverTextThatIsNotANumber_ReturnsTheDefault_WithTypeMismatch()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<int> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.Integer("BrokenNumber", 7));

        Assert.Equal(7, details.Value);
        Assert.Equal(ErrorType.TypeMismatch, details.ErrorType);
        Assert.Equal("Bad", details.Variant);
    }

    [Fact]
    public async Task DisabledFlag_ReturnsItsDefaultWhenDisabledVariant()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<string> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.String("SwitchedOff", "declared-default"));

        Assert.Equal("fallback", details.Value);
        Assert.Equal(Reason.Disabled, details.Reason);
    }

    [Fact]
    public async Task FlagWithNoAllocation_ReturnsTheDeclaredDefault_WithDefaultReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<string> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.String("NoAllocation", "declared-default"));

        Assert.Equal("declared-default", details.Value);
        Assert.Equal(ErrorType.None, details.ErrorType);
        Assert.Equal(Reason.Default, details.Reason);
    }

    [Fact]
    public async Task MissingVariantFlag_ReturnsTheDeclaredDefault_WithFlagNotFound()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<CheckoutSettings?> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.Object<CheckoutSettings?>("Nope", null, TestJsonContext.Default.CheckoutSettings));

        Assert.Null(details.Value);
        Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);
    }

    [Fact]
    public async Task OpenFeaturesOwnObjectApi_ReturnsTheVariantAsAStructure()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        Value value = await provider.NewScopeClient().GetObjectValueAsync("Checkout", new Value());

        Assert.True(value.IsStructure);
        Assert.Equal("Fast checkout", value.AsStructure!.GetValue("title").AsString);
        Assert.Equal(2, value.AsStructure.GetValue("providers").AsList!.Count);
    }
}
