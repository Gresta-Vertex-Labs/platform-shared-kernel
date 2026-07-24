using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// A point-in-time description of a single workflow execution, returned by
/// <see cref="IWorkflowDispatcher.DescribeAsync"/>.
/// </summary>
/// <remarks>
/// This is the only read offered against arbitrary workflow executions — there is deliberately no
/// list/search member backed by Temporal's Visibility API, whose availability and query syntax
/// depend on the server deployment.
/// </remarks>
public sealed record WorkflowExecutionDescription
{
    /// <summary>Gets the workflow id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the run id of the current (or most recent) run.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the workflow type name.</summary>
    public required string WorkflowType { get; init; }

    /// <summary>Gets the task queue the execution is running on.</summary>
    public required string TaskQueue { get; init; }

    /// <summary>Gets the execution's current status.</summary>
    public required WorkflowExecutionStatus Status { get; init; }

    /// <summary>Gets when the execution started.</summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>Gets when the execution closed, if it has.</summary>
    public DateTimeOffset? CloseTime { get; init; }

    /// <summary>Gets the total recorded history length, at the time of the describe call.</summary>
    public int HistoryLength { get; init; }
}
