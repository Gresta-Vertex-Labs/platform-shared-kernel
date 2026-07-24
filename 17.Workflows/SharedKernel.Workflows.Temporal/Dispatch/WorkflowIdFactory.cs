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
        if (tenantScope == TenantScope.None)
        {
            throw new ArgumentException(
                "A workflow id cannot be composed for TenantScope.None.",
                nameof(tenantScope));
        }

        return string.Join(
            WorkflowWellKnown.IdSeparator,
            tenantScope.Value,
            workflowTypeName,
            businessKey);
    }
}
