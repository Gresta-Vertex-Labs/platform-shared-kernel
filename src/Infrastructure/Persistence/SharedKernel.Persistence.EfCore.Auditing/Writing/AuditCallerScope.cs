using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Errors;

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
    /// <exception cref="ForbiddenException">
    /// No tenant is resolved and the caller is not an explicit system scope: the shared
    /// <c>persistence.tenant_required</c> error. The system audit chain (no tenant) is only available to an
    /// authenticated system identity (<c>SystemRequestContext</c>) or inside an active cross-tenant scope; an
    /// unresolved tenant is never treated as the system chain.
    /// </exception>
    public TenantId? ResolveCallerTenant()
    {
        if (requestContext.TenantId is { } tenantId)
            return tenantId;

        if (IsExplicitSystemScope)
            return null;

        throw new ForbiddenException(PersistenceSharedErrors.TenantRequired());
    }

    /// <summary>Gets whether the caller may act on the chains of <paramref name="tenantId"/>.</summary>
    public bool CanAccess(TenantId? tenantId) =>
        crossTenantScope.IsActive ||
        (tenantId is { } id ? id == requestContext.TenantId : requestContext.TenantId is null && IsExplicitSystemScope);

    /// <summary>Throws unless a cross-tenant scope is active.</summary>
    /// <exception cref="InvalidOperationException">
    /// No cross-tenant scope is active: a programming error of the caller, so the message names the operation only.
    /// </exception>
    public void RequireCrossTenantScope(string operation)
    {
        if (!crossTenantScope.IsActive)
        {
            throw new InvalidOperationException(
                $"'{operation}' reaches across tenants and requires an active cross-tenant scope. " +
                "Enter one through ICrossTenantScope around this call.");
        }
    }

    /// <summary>Throws unless the caller may act on the chains of <paramref name="tenantId"/>.</summary>
    /// <exception cref="ForbiddenException">
    /// The chains are another tenant's and no cross-tenant scope is active: <c>persistence.tenant_isolation_violation</c>,
    /// with no tenant id in the text.
    /// </exception>
    public void RequireAccess(TenantId? tenantId)
    {
        if (!CanAccess(tenantId))
            throw new ForbiddenException(AuditErrors.OtherTenantChain());
    }
}
