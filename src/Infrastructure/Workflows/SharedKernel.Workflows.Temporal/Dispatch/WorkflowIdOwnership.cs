using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Constants;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// Decides whether a composed workflow id belongs to a tenant. Companion of <see cref="WorkflowIdFactory"/>: the
/// factory writes the tenant as the first segment of <c>{tenant}:{workflowType}:{businessKey}</c>, this helper reads
/// it back.
/// </summary>
/// <remarks>
/// A <see cref="TenantId"/> is a GUID in its hyphenated <c>"D"</c> form, which never contains
/// <see cref="WorkflowWellKnown.IdSeparator"/>, so comparing the id's prefix with the caller's tenant plus the
/// separator cannot match a longer tenant segment.
/// </remarks>
internal static class WorkflowIdOwnership
{
    /// <summary>
    /// Whether <paramref name="workflowId"/> starts with <paramref name="tenantScope"/>'s tenant segment.
    /// </summary>
    /// <param name="workflowId">The composed workflow id.</param>
    /// <param name="tenantScope">The caller's tenant scope.</param>
    /// <returns><see langword="false"/> for <see cref="TenantScope.Global"/> or a foreign tenant's id.</returns>
    public static bool IsOwnedBy(string workflowId, TenantScope tenantScope) =>
        tenantScope.Tenant is { } tenant
        && workflowId.StartsWith(tenant.ToString() + WorkflowWellKnown.IdSeparator, StringComparison.Ordinal);
}
