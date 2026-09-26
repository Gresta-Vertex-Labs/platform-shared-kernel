using SharedKernel.Execution.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Resolves the tenant asserted by the authenticated caller's credential, through the
/// <see cref="IUserContextMapper"/> the authentication package registered.
/// </summary>
/// <remarks>
/// A thin adapter only: which claim carries the tenant is decided by the authentication package (for example
/// <c>SharedKernel:Security:Oidc:Claims:TenantClaimType</c>). Requires <see cref="HttpContext.User"/> to be
/// populated, so the owning <see cref="Middleware.TenantResolutionMiddleware"/> must run after
/// <c>UseAuthentication()</c>.
/// </remarks>
public sealed class ClaimTenantResolutionStrategy : ITenantResolutionStrategy
{
    /// <inheritdoc/>
    public string StrategyName => TenantResolutionStrategyNames.Claim;

    /// <inheritdoc/>
    public Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IEnumerable<IUserContextMapper> mappers = context.RequestServices?.GetServices<IUserContextMapper>() ?? [];
        return Task.FromResult(UserContextResolver.Resolve(context.User, mappers).TenantId);
    }
}
