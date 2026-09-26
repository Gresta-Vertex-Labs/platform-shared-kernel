using System.Collections.Concurrent;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Testing.Workflows;

/// <summary>
/// In-memory test double for <see cref="IWorkflowHandle"/>. Every handle obtained for the same
/// workflow id -- whether from <see cref="InMemoryWorkflowDispatcher.StartAsync{TWorkflow}"/> or
/// <see cref="InMemoryWorkflowDispatcher.GetHandle(string, string?, TenantScope)"/> -- is a thin view
/// over the SAME shared backing execution; it is never an independent copy.
/// </summary>
public sealed class InMemoryWorkflowHandle : IWorkflowHandle
{
    private readonly ConcurrentDictionary<string, InMemoryWorkflowExecution> _executions;

    internal InMemoryWorkflowHandle(
        ConcurrentDictionary<string, InMemoryWorkflowExecution> executions, string workflowId, string? runId)
    {
        _executions = executions;
        WorkflowId = workflowId;
        RunId = runId;
    }

    /// <inheritdoc />
    public string WorkflowId { get; }

    /// <inheritdoc />
    public string? RunId { get; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="SignalAsync{TSignalArgs}"/>,
    /// <see cref="CancelAsync"/>, and <see cref="TerminateAsync"/> should simulate a Temporal service
    /// outage instead of performing the operation. <see cref="QueryAsync{TQueryResult}"/> -- a read --
    /// is unaffected, mirroring the <c>Storage/</c>/<c>Search/</c>/<c>Intelligence/</c>
    /// <c>SimulateFailure</c> convention exactly.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets the execution's current lifecycle status, or <see langword="null"/> if no backing
    /// execution has ever been created for <see cref="WorkflowId"/> (see the "never round-trips" note
    /// on <see cref="InMemoryWorkflowDispatcher.GetHandle(string, string?, TenantScope)"/>).
    /// </summary>
    public WorkflowLifecycleStatus? Status => TryGetExecution(out var execution) ? execution!.Status : null;

    /// <summary>Every signal ever delivered to this execution, in delivery order.</summary>
    public IReadOnlyList<(string SignalName, object? Args)> SignalsReceived =>
        TryGetExecution(out var execution) ? execution!.SignalHistory.ToArray() : [];

    /// <summary>Every query name ever dispatched against this execution, in dispatch order.</summary>
    public IReadOnlyList<string> QueriesReceived =>
        TryGetExecution(out var execution) ? execution!.QueriesReceived.ToArray() : [];

    /// <inheritdoc />
    public Task<Result> SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.ServiceUnavailable("SimulateFailure is enabled.")));
        }

        if (!TryGetExecution(out var execution))
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.NotFound(WorkflowId)));
        }

        execution!.SignalHistory.Enqueue((signalName, args));
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<TQueryResult>> QueryAsync<TQueryResult>(
        string queryName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);

        if (!TryGetExecution(out var execution))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<TQueryResult>.Failure(WorkflowErrors.NotFound(WorkflowId)));
        }

        execution!.QueriesReceived.Enqueue(queryName);

        // The fake's direct analogue of the real contract's own "a query against an execution whose
        // worker fleet is down returns QueryFailed rather than hanging": an unconfigured query name
        // never blocks, it fails explicitly.
        if (!execution.QueryHandlers.TryGetValue(queryName, out var handler))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<TQueryResult>.Failure(
                WorkflowErrors.QueryFailed($"No query handler configured for '{queryName}'.")));
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result<TQueryResult>.Success((TQueryResult)handler()!));
    }

    /// <inheritdoc />
    public Task<Result> CancelAsync(CancellationToken cancellationToken = default)
    {
        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.ServiceUnavailable("SimulateFailure is enabled.")));
        }

        if (!TryGetExecution(out var execution))
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.NotFound(WorkflowId)));
        }

        // Idempotent -- re-cancelling an already-terminal execution still succeeds without changing
        // Status, a fake-only simplification pending 17.Workflows's own Core phase documenting real
        // double-cancel semantics (mirrors the identical open item Storage/ and Search/ both carry for
        // their own delete-path idempotency).
        if (execution!.Status == WorkflowLifecycleStatus.Running)
        {
            execution.Status = WorkflowLifecycleStatus.Cancelled;
        }

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result> TerminateAsync(string reason, CancellationToken cancellationToken = default)
    {
        // A caller programming error, not a Result-encoded business outcome -- the real
        // IWorkflowHandle.TerminateAsync signature has no default-reason overload specifically so a
        // caller must always supply one.
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.ServiceUnavailable("SimulateFailure is enabled.")));
        }

        if (!TryGetExecution(out var execution))
        {
            return Task.FromResult(Result.Failure(WorkflowErrors.NotFound(WorkflowId)));
        }

        // Idempotent, same rationale as CancelAsync above.
        if (execution!.Status == WorkflowLifecycleStatus.Running)
        {
            execution.Status = WorkflowLifecycleStatus.Terminated;
            execution.TerminationReason = reason;
        }

        return Task.FromResult(Result.Success());
    }

    /// <summary>Returns the args most recently recorded for <paramref name="signalName"/>.</summary>
    /// <exception cref="InvalidOperationException">The execution was never signalled with <paramref name="signalName"/>.</exception>
    public object? ShouldHaveSignalled(string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        foreach (var (name, args) in SignalsReceived)
        {
            if (string.Equals(name, signalName, StringComparison.Ordinal))
            {
                return args;
            }
        }

        throw new InvalidOperationException($"Workflow '{WorkflowId}' was never signalled with '{signalName}'.");
    }

    /// <summary>Typed variant of <see cref="ShouldHaveSignalled(string)"/>.</summary>
    public TSignalArgs ShouldHaveSignalled<TSignalArgs>(string signalName) => (TSignalArgs)ShouldHaveSignalled(signalName)!;

    /// <summary>Asserts <paramref name="signalName"/> was never delivered to this execution.</summary>
    /// <exception cref="InvalidOperationException">A matching signal was recorded.</exception>
    public void ShouldNotHaveSignalled(string signalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalName);
        if (SignalsReceived.Any(s => string.Equals(s.SignalName, signalName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Workflow '{WorkflowId}' was signalled with '{signalName}', but ShouldNotHaveSignalled expected no such signal.");
        }
    }

    /// <summary>Asserts this execution was queried with <paramref name="queryName"/> at least once.</summary>
    /// <exception cref="InvalidOperationException">No matching query was recorded.</exception>
    public void ShouldHaveBeenQueried(string queryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        if (!QueriesReceived.Contains(queryName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Workflow '{WorkflowId}' was never queried with '{queryName}'.");
        }
    }

    /// <summary>Asserts this execution's status is <see cref="WorkflowLifecycleStatus.Cancelled"/>.</summary>
    /// <exception cref="InvalidOperationException">The execution was not cancelled.</exception>
    public void ShouldHaveBeenCancelled()
    {
        if (Status != WorkflowLifecycleStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Workflow '{WorkflowId}' was not cancelled (status: {Status?.ToString() ?? "no execution"}).");
        }
    }

    /// <summary>
    /// Asserts this execution's status is <see cref="WorkflowLifecycleStatus.Terminated"/>, and --
    /// when <paramref name="expectedReason"/> is supplied -- that the recorded reason matches exactly.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The execution was not terminated, or (when <paramref name="expectedReason"/> is supplied) the
    /// recorded reason does not match.
    /// </exception>
    public void ShouldHaveBeenTerminated(string? expectedReason = null)
    {
        if (!TryGetExecution(out var execution) || execution!.Status != WorkflowLifecycleStatus.Terminated)
        {
            throw new InvalidOperationException($"Workflow '{WorkflowId}' was not terminated.");
        }

        if (expectedReason is not null && !string.Equals(execution.TerminationReason, expectedReason, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Workflow '{WorkflowId}' was terminated with reason '{execution.TerminationReason}', expected '{expectedReason}'.");
        }
    }

    /// <summary>Attempts to read the shared backing execution for <see cref="WorkflowId"/>.</summary>
    internal bool TryGetExecution(out InMemoryWorkflowExecution? execution) =>
        _executions.TryGetValue(WorkflowId, out execution);
}
