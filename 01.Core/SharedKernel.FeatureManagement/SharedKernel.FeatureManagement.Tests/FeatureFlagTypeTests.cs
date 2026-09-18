using OpenFeature.Model;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>The declaration types on their own.</summary>
public sealed class FeatureFlagTypeTests
{
    [Fact]
    public void Factories_SetKeyKindDefaultAndDescription()
    {
        FeatureFlag<bool> flag = FeatureFlag.Boolean("NewCheckout", description: "The redesigned checkout.");

        Assert.Equal("NewCheckout", flag.Key);
        Assert.Equal(FeatureFlagKind.Boolean, flag.Kind);
        Assert.False(flag.DefaultValue);
        Assert.Equal("The redesigned checkout.", flag.Description);
        Assert.Equal("NewCheckout", flag.ToString());

        Assert.Equal(FeatureFlagKind.String, FeatureFlag.String("a", "x").Kind);
        Assert.Equal(FeatureFlagKind.Integer, FeatureFlag.Integer("a", 1).Kind);
        Assert.Equal(FeatureFlagKind.Double, FeatureFlag.Double("a", 1).Kind);
        Assert.Equal(FeatureFlagKind.Object, FeatureFlag.Object<CheckoutSettings?>("a", null, TestJsonContext.Default.CheckoutSettings).Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankKey_Throws(string key) =>
        Assert.Throws<ArgumentException>(() => FeatureFlag.Boolean(key));

    [Fact]
    public void RequiredArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureFlag.Boolean(null!));
        Assert.Throws<ArgumentNullException>(() => FeatureFlag.String("a", null!));
        Assert.Throws<ArgumentNullException>(() => FeatureFlag.Object<CheckoutSettings?>("a", null, null!));
    }

    [Fact]
    public void TargetingContext_UsesTheUser_ThenTheTenant_AsTheTargetingKey()
    {
        Assert.Equal("alice", new FeatureTargetingContext("alice", "acme").TargetingKey);
        Assert.Equal("acme", new FeatureTargetingContext(null, "acme").TargetingKey);
        Assert.Equal("acme", FeatureTargetingContext.ForTenant("acme").TargetingKey);
        Assert.Null(new FeatureTargetingContext(" ").TargetingKey);
    }

    [Fact]
    public void TargetingContext_DropsBlankAndDuplicateGroups()
    {
        var context = new FeatureTargetingContext("alice", groups: ["beta", "", "beta", "staff", " "]);

        Assert.Equal(["beta", "staff"], context.Groups);
    }

    [Fact]
    public void TargetingContext_BecomesAnOpenFeatureEvaluationContext()
    {
        EvaluationContext context = new FeatureTargetingContext("alice", "acme", ["beta"]).ToEvaluationContext();

        Assert.Equal("alice", context.TargetingKey);
        Assert.Equal("acme", context.GetValue(FeatureContextKeys.TenantId).AsString);
        Assert.Equal("beta", Assert.Single(context.GetValue(FeatureContextKeys.Groups).AsList!).AsString);
    }

    [Fact]
    public void AnEmptyTargetingContext_IsAnEmptyEvaluationContext()
    {
        EvaluationContext context = new FeatureTargetingContext(null).ToEvaluationContext();

        Assert.Null(context.TargetingKey);
        Assert.Equal(0, context.Count);
    }

    [Fact]
    public void ForTenant_RejectsABlankId() =>
        Assert.Throws<ArgumentException>(() => FeatureTargetingContext.ForTenant(" "));

    [Fact]
    public void ValidationException_ListsEveryFailure()
    {
        var error = new FeatureFlagValidationException(["'A' is not configured.", "'B' is not configured."]);

        Assert.Equal(2, error.Failures.Count);
        Assert.Equal(
            "Feature flag configuration is invalid:" + Environment.NewLine + "- 'A' is not configured." + Environment.NewLine + "- 'B' is not configured.",
            error.Message);
    }
}
