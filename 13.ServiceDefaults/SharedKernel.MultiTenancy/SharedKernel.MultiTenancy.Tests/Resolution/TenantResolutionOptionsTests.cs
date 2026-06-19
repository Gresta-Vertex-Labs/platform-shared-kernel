using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class TenantResolutionOptionsTests
{
    [Fact]
    public void StrategyOrder_DefaultsTo_HeaderClaimDatabase()
    {
        var options = new TenantResolutionOptions();

        Assert.Equal(["Header", "Claim", "Database"], options.StrategyOrder);
    }

    [Fact]
    public void SectionName_IsExpectedConfigurationKey()
    {
        Assert.Equal("SharedKernel:MultiTenancy", TenantResolutionOptions.SectionName);
    }
}
