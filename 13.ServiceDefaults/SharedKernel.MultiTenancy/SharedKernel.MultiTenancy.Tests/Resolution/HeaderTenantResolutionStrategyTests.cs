using Microsoft.AspNetCore.Http;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class HeaderTenantResolutionStrategyTests
{
    [Fact]
    public void DefaultHeaderName_SourcedFromWellKnownHeaders_StillEqualsXTenantId()
    {
        // Regression check: DefaultHeaderName's authoritative source moved to
        // 01.Core's WellKnownHeaders.TenantId, but the literal value itself must remain unchanged —
        // this proves a source-of-truth relocation, not a behavior change.
        Assert.Equal("X-Tenant-Id", HeaderTenantResolutionStrategy.DefaultHeaderName);
        Assert.Equal(WellKnownHeaders.TenantId, HeaderTenantResolutionStrategy.DefaultHeaderName);
    }

    [Fact]
    public async Task TryResolveAsync_WithParseableHeader_ReturnsGuid()
    {
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var strategy = new HeaderTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result);
    }

    [Fact]
    public async Task TryResolveAsync_WithAbsentHeader_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        var strategy = new HeaderTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_WithMalformedHeader_ReturnsNullAndDoesNotThrow()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = "not-a-guid";

        var strategy = new HeaderTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_WithCustomHeaderName_UsesConfiguredHeader()
    {
        var tenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Custom-Tenant"] = tenantId.ToString();

        var strategy = new HeaderTenantResolutionStrategy("X-Custom-Tenant");

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result);
    }
}
