using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Security.Abstractions.Claims;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class ClaimTenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_WithValidTenantClaim_ReturnsGuid()
    {
        var tenantId = Guid.NewGuid();
        var claims = new[] { new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new DefaultHttpContext { User = principal };

        var strategy = new ClaimTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result);
    }

    [Fact]
    public async Task TryResolveAsync_WithNoTenantClaim_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var context = new DefaultHttpContext { User = principal };

        var strategy = new ClaimTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_WithMalformedTenantClaim_ReturnsNull()
    {
        var claims = new[] { new Claim(SecurityClaimTypes.TenantId, "not-a-guid") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new DefaultHttpContext { User = principal };

        var strategy = new ClaimTenantResolutionStrategy();

        var result = await strategy.TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }
}
