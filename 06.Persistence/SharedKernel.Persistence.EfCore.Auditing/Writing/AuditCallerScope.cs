using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.EfCore.Auditing.Writing;

/// <summary>
/// Which chains the caller may touch (A16): its own tenant's; the system chain only as an authenticated
/// system identity; any chain inside an active cross-tenant scope. An unresolved tenant is never treated
/// as the system chain.
/// </summary>
internal sealed class AuditCallerScope(IRequestContext requestContext, ICrossTenantScope crossTenantScope)
{
    public IRequestContext RequestContext => requestContext;

    public bool CrossTenantActive => crossTenantScope.IsActive;

    /// <summary>Gets whether the caller may act on the system chain.</summary>
    public bool IsExplicitSystemScope =>
        crossTenantScope.IsActive || (requestContext.IsAuthenticated && requestContext.ActorKind == ActorKind.System);

    /// <summary>Returns the caller's tenant, or <see langword="null"/> (system chain) for an explicit system scope.</summary>
    /// <exception cref="InvalidOperationException">No tenant is resolved and the caller is not an explicit system scope.</exception>
    public Guid? ResolveCallerTenant(string operation)
    {
        if (requestContext.TenantId is { } tenantId)
            return tenantId;

        if (IsExplicitSystemScope)
            return null;

        throw new InvalidOperationException(
            $"{operation} needs a tenant, but the request context resolved none. The system audit chain (no tenant) is " +
            "only available to an authenticated system identity (SystemRequestContext) or inside an active cross-tenant " +
            "scope; an unresolved tenant is never treated as the system chain.");
    }

    /// <summary>Gets whether the caller may act on the chains of <paramref name="tenantId"/>.</summary>
    public bool CanAccess(Guid? tenantId) =>
        crossTenantScope.IsActive ||
        (tenantId is { } id ? id == requestContext.TenantId : requestContext.TenantId is null && IsExplicitSystemScope);

    /// <summary>Throws unless a cross-tenant scope is active.</summary>
    public void RequireCrossTenantScope(string operation)
    {
        if (!crossTenantScope.IsActive)
        {
            throw new InvalidOperationException(
                $"'{operation}' reaches across tenants and requires an active cross-tenant scope " +
                "(enter one through ICrossTenantScope around this call).");
        }
    }

    /// <summary>Throws unless the caller may act on the chains of <paramref name="tenantId"/>.</summary>
    public void RequireAccess(Guid? tenantId, string operation)
    {
        if (!CanAccess(tenantId))
        {
            throw new InvalidOperationException(
                $"'{operation}' targets the audit chains of {(tenantId?.ToString("D") ?? "the system")}, which is not the caller's " +
                "tenant; enter a cross-tenant scope to do this.");
        }
    }
}
