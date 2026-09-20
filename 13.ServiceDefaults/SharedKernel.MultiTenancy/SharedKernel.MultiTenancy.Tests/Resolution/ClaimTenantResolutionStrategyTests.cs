using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.MultiTenancy.Tests.Resolution;

public sealed class ClaimTenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_WithValidTenantClaim_ReturnsGuid()
    {
        var tenantId = Guid.NewGuid();
        var context = CreateContext(new ClaimsIdentity([new Claim(SecurityClaimTypes.TenantId, tenantId.ToString())], "Bearer"));

        var result = await new ClaimTenantResolutionStrategy().TryResolveAsync(context, CancellationToken.None);

        Assert.Equal(tenantId, result);
    }

    [Fact]
    public async Task TryResolveAsync_WithNoTenantClaim_ReturnsNull()
    {
        var context = CreateContext(new ClaimsIdentity());

        var result = await new ClaimTenantResolutionStrategy().TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_WithMalformedTenantClaim_ReturnsNull()
    {
        var context = CreateContext(new ClaimsIdentity([new Claim(SecurityClaimTypes.TenantId, "not-a-guid")], "Bearer"));

        var result = await new ClaimTenantResolutionStrategy().TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveAsync_WithUnmappedScheme_ReturnsNull()
    {
        var context = CreateContext(new ClaimsIdentity([new Claim(SecurityClaimTypes.TenantId, Guid.NewGuid().ToString())], "Cookies"));

        var result = await new ClaimTenantResolutionStrategy().TryResolveAsync(context, CancellationToken.None);

        Assert.Null(result);
    }

    private static DefaultHttpContext CreateContext(ClaimsIdentity identity) => new()
    {
        User = new ClaimsPrincipal(identity),
        RequestServices = new ServiceCollection()
            .AddSingleton<IUserContextMapper, TenantClaimMapper>()
                .BuildServiceProvider(),
    };

    private sealed class TenantClaimMapper : IUserContextMapper
    {
        public string AuthenticationType => "Bearer";

        public IUserContext Map(ClaimsIdentity identity) =>
            new UserContext(IdentityKind.User, "subject")
            {
                TenantId = Guid.TryParse(identity.FindFirst(SecurityClaimTypes.TenantId)?.Value, out Guid tenantId) ? tenantId : null,
            };
    }
}
