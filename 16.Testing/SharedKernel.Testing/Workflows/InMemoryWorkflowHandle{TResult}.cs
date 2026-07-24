using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Testing.Workflows;

/// <summary>
/// In-memory test double for <see cref="IWorkflowHandle{TResult}"/>. Composes an
/// <see cref="InMemoryWorkflowHandle"/> internally -- never inherits, since every fake in this package
/// is sealed with no extension point -- and forwards every member but <see cref="GetResultAsync"/> to
/// it verbatim.
/// </summary>
/// <typeparam name="TResult">The workflow's result type.</typeparam>
public sealed class InMemoryWorkflowHandle<TResult> : IWorkflowHandle<TResult>
{
    private readonly InMemoryWorkflowHandle _inner;

    internal InMemoryWorkflowHandle(InMemoryWorkflowHandle inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public string WorkflowId => _inner.WorkflowId;

    /// <inheritdoc />
    public string? RunId => _inner.RunId;

    /// <summary>Forwards to the composed <see cref="InMemoryWorkflowHandle.SimulateFailure"/>.</summary>
    public bool SimulateFailure
    {
        get => _inner.SimulateFailure;
        set => _inner.SimulateFailure = value;
    }

    /// <summary>Forwards to the composed <see cref="InMemoryWorkflowHandle.Status"/>.</summary>
    public WorkflowLifecycleStatus? Status => _inner.Status;

    /// <summary>Forwards to the composed <see cref="InMemoryWorkflowHandle.SignalsReceived"/>.</summary>
    public IReadOnlyList<(string SignalName, object? Args)> SignalsReceived => _inner.SignalsReceived;

    /// <summary>Forwards to the composed <see cref="InMemoryWorkflowHandle.QueriesReceived"/>.</summary>
    public IReadOnlyList<string> QueriesReceived => _inner.QueriesReceived;

    /// <inheritdoc />
    public Task<Result> SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken cancellationToken = default) =>
        _inner.SignalAsync(signalName, args, cancellationToken);

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<TQueryResult>> QueryAsync<TQueryResult>(
        string queryName, CancellationToken cancellationToken = default) =>
        _inner.QueryAsync<TQueryResult>(queryName, cancellationToken);

    /// <inheritdoc />
    public Task<Result> CancelAsync(CancellationToken cancellationToken = default) => _inner.CancelAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Result> TerminateAsync(string reason, CancellationToken cancellationToken = default) =>
        _inner.TerminateAsync(reason, cancellationToken);

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldHaveSignalled(string)"/>.</summary>
    public object? ShouldHaveSignalled(string signalName) => _inner.ShouldHaveSignalled(signalName);

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldHaveSignalled{TSignalArgs}"/>.</summary>
    public TSignalArgs ShouldHaveSignalled<TSignalArgs>(string signalName) => _inner.ShouldHaveSignalled<TSignalArgs>(signalName);

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldNotHaveSignalled"/>.</summary>
    public void ShouldNotHaveSignalled(string signalName) => _inner.ShouldNotHaveSignalled(signalName);

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldHaveBeenQueried"/>.</summary>
    public void ShouldHaveBeenQueried(string queryName) => _inner.ShouldHaveBeenQueried(queryName);

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldHaveBeenCancelled"/>.</summary>
    public void ShouldHaveBeenCancelled() => _inner.ShouldHaveBeenCancelled();

    /// <summary>Forwards to <see cref="InMemoryWorkflowHandle.ShouldHaveBeenTerminated"/>.</summary>
    public void ShouldHaveBeenTerminated(string? expectedReason = null) => _inner.ShouldHaveBeenTerminated(expectedReason);

    /// <inheritdoc />
    /// <remarks>
    /// Deterministic, never blocks, never polls -- the fake never executes a workflow, so there is no
    /// real completion event to await. Reads the backing execution's configured eventual result, set
    /// only via <see cref="InMemoryWorkflowDispatcher.CompleteWorkflow{TResult}"/>/
    /// <see cref="InMemoryWorkflowDispatcher.FailWorkflow"/>, and returns it immediately if present.
    /// Calling this before either was invoked for this workflow id throws
    /// <see cref="InvalidOperationException"/> naming the workflow id -- never blocks, never spins,
    /// never <c>Task.Delay</c>-polls waiting for a result this fake has no mechanism to ever produce on
    /// its own.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A backing execution exists for <see cref="WorkflowId"/>, but neither
    /// <see cref="InMemoryWorkflowDispatcher.CompleteWorkflow{TResult}"/> nor
    /// <see cref="InMemoryWorkflowDispatcher.FailWorkflow"/> has been called for it yet.
    /// </exception>
    public Task<SharedKernel.Primitives.Results.Result<TResult>> GetResultAsync(CancellationToken cancellationToken = default)
    {
        if (!_inner.TryGetExecution(out var execution))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<TResult>.Failure(WorkflowErrors.NotFound(WorkflowId)));
        }

        if (!execution!.TryGetResult(out var value, out var isFailure))
        {
            throw new InvalidOperationException(
                $"Workflow execution '{WorkflowId}' has no pending result. Call " +
                $"InMemoryWorkflowDispatcher.CompleteWorkflow<TResult>/.FailWorkflow for this workflow id " +
                "before calling GetResultAsync.");
        }

        return Task.FromResult(isFailure
            ? SharedKernel.Primitives.Results.Result<TResult>.Failure((SharedKernel.Primitives.Errors.Error)value!)
            : SharedKernel.Primitives.Results.Result<TResult>.Success((TResult)value!));
    }
}
