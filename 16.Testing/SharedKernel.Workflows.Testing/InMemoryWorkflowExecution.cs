using System.Collections.Concurrent;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Testing.Workflows;

/// <summary>
/// The lifecycle status of a fake workflow execution tracked by
/// <see cref="InMemoryWorkflowDispatcher"/> -- never derived from a real Temporal server, since this
/// test double never talks to one.
/// </summary>
/// <remarks>
/// Declared <c>public</c> rather than <c>internal</c>: it is the return type of
/// <see cref="InMemoryWorkflowHandle.Status"/> and <see cref="InMemoryWorkflowHandle{TResult}.Status"/>,
/// both public members a consuming test asserts against directly. The original design draft
/// (16.Testing/CLAUDE.md) framed this as an "internal enum" before <c>17.Workflows</c> shipped
/// compiled code to verify against -- this is a corrected, implementer-verified shape.
/// </remarks>
public enum WorkflowLifecycleStatus
{
    /// <summary>The execution is running and has not yet reached a terminal state.</summary>
    Running,

    /// <summary>The execution completed successfully via <see cref="InMemoryWorkflowDispatcher.CompleteWorkflow{TResult}"/>.</summary>
    Completed,

    /// <summary>The execution was cancelled via <see cref="InMemoryWorkflowHandle.CancelAsync"/>.</summary>
    Cancelled,

    /// <summary>The execution was terminated via <see cref="InMemoryWorkflowHandle.TerminateAsync"/>.</summary>
    Terminated,

    /// <summary>The execution completed with a failure via <see cref="InMemoryWorkflowDispatcher.FailWorkflow"/>.</summary>
    Failed,
}

/// <summary>
/// The shared, mutable backing state for a single fake workflow execution -- never exposed publicly.
/// Every <see cref="InMemoryWorkflowHandle"/>/<see cref="InMemoryWorkflowHandle{TResult}"/> obtained
/// for the same workflow id is a thin view over the SAME instance, never an independent copy.
/// </summary>
/// <remarks>
/// Implemented as a mutable sealed class rather than an immutable record: an execution's
/// <see cref="Status"/>, signal/query history, and eventual result all mutate in place as the fake's
/// dispatcher/handle members are called, which a record's value-equality/with-expression semantics do
/// not fit -- a deliberate, documented divergence from the original design draft's "internal state
/// record" wording. Every mutable member is either a <see cref="ConcurrentQueue{T}"/>/
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>, or backed by <see cref="Volatile"/>/
/// <see cref="Interlocked"/> access, since xUnit runs test collections in parallel by default.
/// </remarks>
internal sealed class InMemoryWorkflowExecution
{
    /// <summary>Gets the composed workflow id.</summary>
    public required string WorkflowId { get; init; }

    /// <summary>Gets the fake's own deterministic, incrementing run id -- never a real Temporal run id.</summary>
    public required string RunId { get; init; }

    /// <summary>Gets the task queue the execution was started against.</summary>
    public required string TaskQueue { get; init; }

    /// <summary>Gets the tenant scope the execution was started under.</summary>
    public required TenantScope TenantScope { get; init; }

    /// <summary>Gets <c>typeof(TWorkflow).Name</c> for the workflow type this execution was started for.</summary>
    public required string WorkflowTypeName { get; init; }

    /// <summary>Gets the workflow argument, or <see langword="null"/> for a no-argument start.</summary>
    public object? Args { get; init; }

    private int _status = (int)WorkflowLifecycleStatus.Running;

    /// <summary>Gets or sets the execution's current lifecycle status.</summary>
    public WorkflowLifecycleStatus Status
    {
        get => (WorkflowLifecycleStatus)Volatile.Read(ref _status);
        set => Volatile.Write(ref _status, (int)value);
    }

    private string? _terminationReason;

    /// <summary>
    /// Gets or sets the reason recorded by the most recent successful
    /// <see cref="InMemoryWorkflowHandle.TerminateAsync"/> call, if any.
    /// </summary>
    public string? TerminationReason
    {
        get => Volatile.Read(ref _terminationReason);
        set => Volatile.Write(ref _terminationReason, value);
    }

    /// <summary>Every signal ever delivered to this execution, in delivery order.</summary>
    public ConcurrentQueue<(string SignalName, object? Args)> SignalHistory { get; } = new();

    /// <summary>Every query name ever dispatched against this execution, in dispatch order.</summary>
    public ConcurrentQueue<string> QueriesReceived { get; } = new();

    /// <summary>
    /// Per-query-name handlers configured via
    /// <see cref="InMemoryWorkflowDispatcher.ConfigureQueryHandler{TQueryResult}"/>, consulted by
    /// <see cref="InMemoryWorkflowHandle.QueryAsync{TQueryResult}"/>.
    /// </summary>
    public ConcurrentDictionary<string, Func<object?>> QueryHandlers { get; } = new(StringComparer.Ordinal);

    private PendingResultState? _pendingResult;

    /// <summary>
    /// Sets the execution's eventual result, via
    /// <see cref="InMemoryWorkflowDispatcher.CompleteWorkflow{TResult}"/> (<paramref name="isFailure"/>
    /// <see langword="false"/>, <paramref name="value"/> the boxed <c>TResult</c>) or
    /// <see cref="InMemoryWorkflowDispatcher.FailWorkflow"/> (<paramref name="isFailure"/>
    /// <see langword="true"/>, <paramref name="value"/> a boxed
    /// <c>SharedKernel.Primitives.Errors.Error</c>).
    /// </summary>
    public void SetResult(object? value, bool isFailure) =>
        Interlocked.Exchange(ref _pendingResult, new PendingResultState(value, isFailure));

    /// <summary>Attempts to read the execution's configured eventual result.</summary>
    /// <returns>
    /// <see langword="true"/> if <see cref="SetResult"/> has been called for this execution;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public bool TryGetResult(out object? value, out bool isFailure)
    {
        var state = Volatile.Read(ref _pendingResult);
        if (state is null)
        {
            value = null;
            isFailure = false;
            return false;
        }

        value = state.Value;
        isFailure = state.IsFailure;
        return true;
    }

    private sealed record PendingResultState(object? Value, bool IsFailure);
}
