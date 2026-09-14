using SharedKernel.ServiceDefaults.Localization;

namespace SharedKernel.ServiceDefaults.Localization.Tests.Localization;

public sealed class LocalizationResolutionOptionsTests
{
    [Fact]
    public void StrategyOrder_DefaultsTo_UserPreference_TenantDefault_AcceptLanguageHeader_InThatOrder()
    {
        var options = new LocalizationResolutionOptions();

        Assert.Equal(
            [
                LocalizationResolutionStrategy.UserPreference,
                LocalizationResolutionStrategy.TenantDefault,
                LocalizationResolutionStrategy.AcceptLanguageHeader,
            ],
            options.StrategyOrder);
    }

    [Fact]
    public void UserPreferenceClaimType_DefaultsToNull()
    {
        var options = new LocalizationResolutionOptions();

        Assert.Null(options.UserPreferenceClaimType);
    }
}
