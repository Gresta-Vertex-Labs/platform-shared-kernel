using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// A minimal test-only marker type standing in for <c>TWorkflow</c> across the <c>Workflows/</c>
/// self-tests. <see cref="InMemoryWorkflowDispatcher"/> never instantiates or executes workflow code
/// -- it only reads <c>typeof(TWorkflow).Name</c> -- so a plain, otherwise-empty subclass of
/// <see cref="WorkflowBase"/> is sufficient; it never touches <c>Workflow.Logger</c>/<c>Workflow.Info</c>
/// or any other ambient Temporal replay context.
/// </summary>
internal sealed class SampleWorkflow : WorkflowBase
{
}

/// <summary>A second marker workflow type, distinct from <see cref="SampleWorkflow"/>, for multi-type assertions.</summary>
internal sealed class OtherWorkflow : WorkflowBase
{
}

/// <summary>Shared option/arg builders for the <c>Workflows/</c> self-tests.</summary>
internal static class WorkflowsTestFixtures
{
    public static readonly TenantId TenantA = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-0000000000a1"));

    public static readonly TenantId TenantB = new(Guid.Parse("0f6b1c2e-5d4a-4f7b-9a01-0000000000b2"));

    public static WorkflowStartOptions ValidOptions(string businessKey = "business-key-1", string taskQueue = "test-queue") => new()
    {
        TaskQueue = taskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };
}
