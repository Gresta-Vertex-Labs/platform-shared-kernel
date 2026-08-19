using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class TenantResolutionOptionsTests
{
    [Fact]
    public void StrategyOrder_DefaultsTo_ClaimHeaderDatabase()
    {
        // Security-motivated default (WO-061/P-393) — Claim must outrank Header so a
        // cryptographically-verified JWT tenant claim always wins over an unsigned, caller-supplied
        // X-Tenant-Id header for the same request. See TenantResolutionOptions.StrategyOrder's own
        // XML doc remarks for the full rationale — never revert this order without a security review.
        var options = new TenantResolutionOptions();

        Assert.Equal(["Claim", "Header", "Database"], options.StrategyOrder);
    }

    [Fact]
    public void SectionName_IsExpectedConfigurationKey()
    {
        Assert.Equal("SharedKernel:MultiTenancy", TenantResolutionOptions.SectionName);
    }
}
