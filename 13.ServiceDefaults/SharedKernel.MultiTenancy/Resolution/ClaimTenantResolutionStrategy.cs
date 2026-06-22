using Microsoft.AspNetCore.Http;
using SharedKernel.Security.Oidc.Mapping;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Resolves the tenant identifier from the authenticated request's JWT claims by delegating to
/// <see cref="OidcTenantProvider"/>.
/// </summary>
/// <remarks>
/// A thin adapter only — claim-name parsing belongs exclusively to
/// <c>SharedKernel.Security.Oidc.OidcTenantProvider</c> / <c>SharedKernel.Security.Abstractions.SecurityClaimTypes</c>.
/// This type must never duplicate that logic. Requires <see cref="HttpContext.User"/> to be
/// populated, so the owning <see cref="Middleware.TenantResolutionMiddleware"/> must run after
/// <c>UseAuthentication()</c>.
/// </remarks>
public sealed class ClaimTenantResolutionStrategy : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public string StrategyName => TenantResolutionStrategyNames.Claim;

    /// <inheritdoc/>
    public Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tenantProvider = new OidcTenantProvider(context.User);

        return Task.FromResult(tenantProvider.TenantId == Guid.Empty
            ? (Guid?)null
            : tenantProvider.TenantId);
    }
}
