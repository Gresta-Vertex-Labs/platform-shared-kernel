using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Constants;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// The default <see cref="IWorkflowIdFactory"/> — composes
/// <c>"{tenant}:{workflowType}:{businessKey}"</c>.
/// </summary>
internal sealed class WorkflowIdFactory : IWorkflowIdFactory
{
    /// <inheritdoc />
    public string Create(string workflowTypeName, string businessKey, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessKey);
        if (tenantScope.Tenant is not { } tenant)
        {
            throw new ArgumentException(
                "A workflow id cannot be composed for TenantScope.Global.",
                nameof(tenantScope));
        }

        return string.Join(
            WorkflowWellKnown.IdSeparator,
            tenant.ToString(),
            workflowTypeName,
            businessKey);
    }
}
