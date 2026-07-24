namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// Composes the physical Temporal workflow id from a workflow type, a caller-supplied business key,
/// and a tenant scope. Every <see cref="IWorkflowDispatcher"/> start routes through this factory —
/// no dispatch member accepts a raw, caller-supplied workflow id.
/// </summary>
/// <remarks>
/// Registered as a singleton and performs zero I/O. The default implementation composes
/// <c>"{tenant}:{workflowType}:{businessKey}"</c> using
/// <see cref="Constants.WorkflowWellKnown.IdSeparator"/>, and is replaceable by the consuming
/// service — but the tenant segment must remain non-optional in every implementation. Accepting a
/// raw id instead would make the tenant segment a convention instead of a structure, and
/// conventions are what a hurried call site skips.
/// </remarks>
public interface IWorkflowIdFactory
{
    /// <summary>
    /// Composes the workflow id for the given workflow type, business key, and tenant scope.
    /// </summary>
    /// <param name="workflowTypeName">The registered Temporal workflow type name.</param>
    /// <param name="businessKey">
    /// A stable, caller-supplied business key (e.g. an order id). The same inputs must always
    /// produce the same id — an unstable id silently defeats the durable idempotency guarantee a
    /// workflow id provides.
    /// </param>
    /// <param name="tenantScope">The caller's tenant scope. Must not be <see cref="TenantScope.None"/>.</param>
    /// <returns>The composed workflow id.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="businessKey"/> is null/whitespace, or <paramref name="tenantScope"/> is
    /// <see cref="TenantScope.None"/>.
    /// </exception>
    string Create(string workflowTypeName, string businessKey, TenantScope tenantScope);
}
