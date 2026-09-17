using Microsoft.Extensions.Configuration;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class TenantResolutionOptionsTests
{
    [Fact]
    public void DefaultStrategyOrder_IsClaimHeaderDatabase()
    {
        // Security-motivated default (WO-061/P-393) — Claim must outrank Header so a
        // cryptographically-verified JWT tenant claim always wins over an unsigned, caller-supplied
        // X-Tenant-Id header for the same request. See TenantResolutionOptions.DefaultStrategyOrder's own
        // XML doc remarks for the full rationale — never revert this order without a security review.
        Assert.Equal(["Claim", "Header", "Database"], TenantResolutionOptions.DefaultStrategyOrder);
    }

    [Fact]
    public void StrategyOrder_Unset_IsEmptyAndResolvesToDefault()
    {
        var options = new TenantResolutionOptions();

        Assert.Empty(options.StrategyOrder);
        Assert.Equal(TenantResolutionOptions.DefaultStrategyOrder, options.EffectiveStrategyOrder);
    }

    [Fact]
    public void StrategyOrder_BoundFromConfiguration_ReplacesDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TenantResolutionOptions.SectionName}:StrategyOrder:0"] = "Header",
            })
            .Build();
        var options = new TenantResolutionOptions();

        configuration.GetSection(TenantResolutionOptions.SectionName).Bind(options);

        Assert.Equal(["Header"], options.StrategyOrder);
        Assert.Equal(["Header"], options.EffectiveStrategyOrder);
    }

    [Fact]
    public void StrategyOrder_SetToNull_ResolvesToDefault()
    {
        var options = new TenantResolutionOptions { StrategyOrder = null! };

        Assert.Empty(options.StrategyOrder);
        Assert.Equal(TenantResolutionOptions.DefaultStrategyOrder, options.EffectiveStrategyOrder);
    }

    [Fact]
    public void SectionName_IsExpectedConfigurationKey()
    {
        Assert.Equal("SharedKernel:MultiTenancy", TenantResolutionOptions.SectionName);
    }
}
