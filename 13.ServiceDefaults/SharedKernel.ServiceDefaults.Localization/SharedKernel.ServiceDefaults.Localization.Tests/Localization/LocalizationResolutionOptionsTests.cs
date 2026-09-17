using Microsoft.Extensions.Configuration;
using SharedKernel.ServiceDefaults.Localization;

namespace SharedKernel.ServiceDefaults.Localization.Tests.Localization;

public sealed class LocalizationResolutionOptionsTests
{
    [Fact]
    public void DefaultStrategyOrder_Is_UserPreference_TenantDefault_AcceptLanguageHeader_InThatOrder()
    {
        Assert.Equal(
            [
                LocalizationResolutionStrategy.UserPreference,
                LocalizationResolutionStrategy.TenantDefault,
                LocalizationResolutionStrategy.AcceptLanguageHeader,
            ],
            LocalizationResolutionOptions.DefaultStrategyOrder);
    }

    [Fact]
    public void StrategyOrder_Unset_IsEmptyAndResolvesToDefault()
    {
        var options = new LocalizationResolutionOptions();

        Assert.Empty(options.StrategyOrder);
        Assert.Equal(LocalizationResolutionOptions.DefaultStrategyOrder, options.EffectiveStrategyOrder);
    }

    [Fact]
    public void StrategyOrder_BoundFromConfiguration_ReplacesDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Localization:StrategyOrder:0"] = nameof(LocalizationResolutionStrategy.AcceptLanguageHeader),
            })
            .Build();
        var options = new LocalizationResolutionOptions();

        configuration.GetSection("Localization").Bind(options);

        Assert.Equal([LocalizationResolutionStrategy.AcceptLanguageHeader], options.EffectiveStrategyOrder);
    }

    [Fact]
    public void UserPreferenceClaimType_DefaultsToNull()
    {
        var options = new LocalizationResolutionOptions();

        Assert.Null(options.UserPreferenceClaimType);
    }
}
