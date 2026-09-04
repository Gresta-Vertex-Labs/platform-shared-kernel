using SharedKernel.Idempotency.EfCore.Internal;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Internal;

public sealed class EfCoreTenantScopeTests
{
    [Fact]
    public void Resolve_WithTenant_ReturnsTheTenantId()
    {
        var tenantId = Guid.NewGuid();

        var resolved = EfCoreTenantScope.Resolve(tenantId);

        Assert.Equal(tenantId, resolved);
    }

    [Fact]
    public void Resolve_WithNullTenant_ReturnsTheFixedNonTenantSentinel()
    {
        var resolved = EfCoreTenantScope.Resolve(null);

        Assert.Equal(EfCoreTenantScope.NonTenantSentinel, resolved);
    }

    [Fact]
    public void NonTenantSentinel_IsNotGuidEmpty()
    {
        // A misbehaving accessor implementation could plausibly return Guid.Empty by accident;
        // the sentinel must be distinguishable from that failure mode.
        Assert.NotEqual(Guid.Empty, EfCoreTenantScope.NonTenantSentinel);
    }

    [Fact]
    public void Resolve_NullVersusRealTenant_NeverCollide()
    {
        var realTenantThatHappensToMatchSentinelPattern = EfCoreTenantScope.NonTenantSentinel;

        // Even in the pathological case where a real tenant id equals the sentinel value, Resolve
        // is a pure pass-through for a non-null input — it never re-maps a real tenant id. The
        // actual collision-avoidance guarantee is that a genuinely random Guid.NewGuid() practically
        // never equals this fixed constant, not that Resolve() detects the coincidence.
        Assert.Equal(realTenantThatHappensToMatchSentinelPattern, EfCoreTenantScope.Resolve(realTenantThatHappensToMatchSentinelPattern));
    }
}
