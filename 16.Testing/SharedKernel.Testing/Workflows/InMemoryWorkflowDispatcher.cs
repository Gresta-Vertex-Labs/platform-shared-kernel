using System.Collections.Concurrent;
using System.Globalization;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Constants;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Testing.Workflows;

/// <summary>
/// A record of a single successful workflow start, retained by
/// <see cref="InMemoryWorkflowDispatcher.StartedWorkflows"/> for post-hoc assertions.
/// </summary>
/// <param name="WorkflowTypeName"><c>typeof(TWorkflow).Name</c> for the started workflow type.</param>
/// <param name="WorkflowId">The composed workflow id.</param>
/// <param name="TenantScope">The tenant scope the workflow was started under.</param>
/// <param name="Args">The workflow argument, or <see langword="null"/> for a no-argument start.</param>
public sealed record InMemoryWorkflowStartRecord(
    string WorkflowTypeName,
    string WorkflowId,
    TenantScope TenantScope,
    object? Args);

/// <summary>
/// In-memory test double for <see cref="IWorkflowDispatcher"/>. Never talks to a real Temporal
/// server, and never "runs" a workflow -- <see cref="StartAsync{TWorkflow}"/> and its overloads
/// durably RECORD an execution in a <see cref="ConcurrentDictionary{TKey,TValue}"/>-backed store;
/// driving that execution to its eventual result is an explicit test-setup step via
/// <see cref="CompleteWorkflow{TResult}"/>/<see cref="FailWorkflow"/>. Simulates behavioral
/// correctness (what was started/signalled/queried/cancelled/terminated, under what workflow id) --
/// never real Temporal server timing, worker scheduling, or history replay.
/// </summary>
public sealed class InMemoryWorkflowDispatcher : IWorkflowDispatcher
{
    private static readonly DateTimeOffset FixedStartTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IWorkflowIdFactory? _workflowIdFactory;
    private readonly ConcurrentDictionary<string, InMemoryWorkflowExecution> _executions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<InMemoryWorkflowStartRecord> _startedWorkflows = new();
    private long _runIdSequence;

    /// <summary>Initializes a new <see cref="InMemoryWorkflowDispatcher"/>.</summary>
    /// <param name="workflowIdFactory">
    /// An optional caller-supplied <see cref="IWorkflowIdFactory"/> -- e.g. a test asserting its own
    /// custom id-composition policy. When <see langword="null"/> (the default), this fake composes
    /// ids itself using the SAME default format the real <see cref="IWorkflowIdFactory"/> documents --
    /// <c>"{tenant}:{workflowType}:{businessKey}"</c> via <see cref="WorkflowWellKnown.IdSeparator"/> --
    /// reimplemented here rather than delegated to the real default implementation, which is
    /// <c>internal</c> to <c>SharedKernel.Workflows.Temporal</c> and therefore unreachable from this
    /// assembly.
    /// </param>
    public InMemoryWorkflowDispatcher(IWorkflowIdFactory? workflowIdFactory = null)
    {
        _workflowIdFactory = workflowIdFactory;
    }

    /// <summary>
    /// Gets or sets a value indicating whether every <see cref="StartAsync{TWorkflow}"/> overload and
    /// <see cref="DescribeAsync"/> should simulate a Temporal service outage instead of performing the
    /// operation. <see cref="GetHandle(string, string?, TenantScope)"/> is unaffected -- it never
    /// round-trips even in the real contract.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets every successful workflow start, in start order -- recorded even when the test never
    /// asserts on it, mirroring <c>InMemoryMessageBus</c>'s "records every call" rule.
    /// </summary>
    public IReadOnlyList<InMemoryWorkflowStartRecord> StartedWorkflows => _startedWorkflows.ToArray();

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<IWorkflowHandle>> StartAsync<TWorkflow>(
        WorkflowStartOptions options, TenantScope tenantScope, CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
        => Task.FromResult(StartCore<TWorkflow>(args: null, options, tenantScope));

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<IWorkflowHandle>> StartAsync<TWorkflow, TArgs>(
        TArgs args, WorkflowStartOptions options, TenantScope tenantScope, CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
        => Task.FromResult(StartCore<TWorkflow>(args, options, tenantScope));

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<IWorkflowHandle<TResult>>> StartAsync<TWorkflow, TArgs, TResult>(
        TArgs args, WorkflowStartOptions options, TenantScope tenantScope, CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
    {
        var result = StartCore<TWorkflow>(args, options, tenantScope);
        if (result.IsFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<IWorkflowHandle<TResult>>.Failure(result.Error));
        }

        var typed = new InMemoryWorkflowHandle<TResult>((InMemoryWorkflowHandle)result.Value);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<IWorkflowHandle<TResult>>.Success(typed));
    }

    /// <inheritdoc />
    public IWorkflowHandle GetHandle(string workflowId, string? runId, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (tenantScope.IsGlobal)
        {
            throw new ArgumentException("TenantScope.Global is not permitted.", nameof(tenantScope));
        }

        // Never round-trips -- constructs unconditionally regardless of whether a backing execution
        // exists yet, matching real Temporal semantics exactly (a handle's existence is not checked
        // until an operation is invoked against it).
        return new InMemoryWorkflowHandle(_executions, workflowId, runId);
    }

    /// <inheritdoc />
    public IWorkflowHandle<TResult> GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope)
    {
        var inner = (InMemoryWorkflowHandle)GetHandle(workflowId, runId, tenantScope);
        return new InMemoryWorkflowHandle<TResult>(inner);
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<WorkflowExecutionDescription>> DescribeAsync(
        string workflowId, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        if (tenantScope.IsGlobal)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<WorkflowExecutionDescription>.Failure(
                WorkflowErrors.TenantScopeMissing()));
        }

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<WorkflowExecutionDescription>.Failure(
                WorkflowErrors.ServiceUnavailable("SimulateFailure is enabled.")));
        }

        if (!_executions.TryGetValue(workflowId, out var execution))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<WorkflowExecutionDescription>.Failure(
                WorkflowErrors.NotFound(workflowId)));
        }

        var description = new WorkflowExecutionDescription
        {
            Id = execution.WorkflowId,
            RunId = execution.RunId,
            WorkflowType = execution.WorkflowTypeName,
            TaskQueue = execution.TaskQueue,
            Status = MapStatus(execution.Status),
            StartTime = FixedStartTime,
            CloseTime = execution.Status == WorkflowLifecycleStatus.Running ? null : FixedStartTime,
            HistoryLength = 0,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<WorkflowExecutionDescription>.Success(description));
    }

    /// <summary>
    /// Registers a query handler for <paramref name="queryName"/> against an already-started
    /// execution -- the test-setup counterpart <see cref="InMemoryWorkflowHandle.QueryAsync{TQueryResult}"/>
    /// consults. An unconfigured query name returns <c>WorkflowErrors.QueryFailed</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No execution exists for <paramref name="workflowId"/>.</exception>
    public void ConfigureQueryHandler<TQueryResult>(string workflowId, string queryName, Func<TQueryResult> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        ArgumentNullException.ThrowIfNull(handler);

        var execution = RequireExecution(workflowId);
        execution.QueryHandlers[queryName] = () => handler();
    }

    /// <summary>
    /// Marks the workflow execution identified by <paramref name="workflowId"/> as completed with
    /// <paramref name="result"/>, so a later <see cref="InMemoryWorkflowHandle{TResult}.GetResultAsync"/>
    /// call returns it. The fake never "runs" a workflow itself; this is the deliberate, explicit
    /// substitute for real execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">No execution exists for <paramref name="workflowId"/>.</exception>
    public void CompleteWorkflow<TResult>(string workflowId, TResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var execution = RequireExecution(workflowId);
        execution.Status = WorkflowLifecycleStatus.Completed;
        execution.SetResult(result, isFailure: false);
    }

    /// <summary>
    /// The eventual-result analogue of <see cref="CompleteWorkflow{TResult}"/>, for a workflow that is
    /// to be observed as having failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">No execution exists for <paramref name="workflowId"/>.</exception>
    public void FailWorkflow(string workflowId, SharedKernel.Primitives.Errors.Error error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        var execution = RequireExecution(workflowId);
        execution.Status = WorkflowLifecycleStatus.Failed;
        execution.SetResult(error, isFailure: true);
    }

    /// <summary>Returns the first recorded start of workflow type <typeparamref name="TWorkflow"/>.</summary>
    /// <exception cref="InvalidOperationException">No such start was ever recorded.</exception>
    public InMemoryWorkflowStartRecord ShouldHaveStarted<TWorkflow>()
        where TWorkflow : WorkflowBase
    {
        var typeName = typeof(TWorkflow).Name;
        foreach (var record in StartedWorkflows)
        {
            if (string.Equals(record.WorkflowTypeName, typeName, StringComparison.Ordinal))
            {
                return record;
            }
        }

        throw new InvalidOperationException($"No workflow of type '{typeName}' was ever started.");
    }

    /// <summary>Returns the single recorded start of workflow type <typeparamref name="TWorkflow"/>.</summary>
    /// <exception cref="InvalidOperationException">Zero, or more than one, such start was recorded.</exception>
    public InMemoryWorkflowStartRecord ShouldHaveStartedOnce<TWorkflow>()
        where TWorkflow : WorkflowBase
    {
        var typeName = typeof(TWorkflow).Name;
        var matches = StartedWorkflows
            .Where(record => string.Equals(record.WorkflowTypeName, typeName, StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one start of workflow type '{typeName}', found {matches.Count}.");
        }

        return matches[0];
    }

    /// <summary>Asserts that workflow type <typeparamref name="TWorkflow"/> was never started.</summary>
    /// <exception cref="InvalidOperationException">A start of that type was recorded.</exception>
    public void ShouldNotHaveStarted<TWorkflow>()
        where TWorkflow : WorkflowBase
    {
        var typeName = typeof(TWorkflow).Name;
        if (StartedWorkflows.Any(record => string.Equals(record.WorkflowTypeName, typeName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Workflow of type '{typeName}' was started, but ShouldNotHaveStarted expected no such start.");
        }
    }

    /// <summary>Clears the backing execution store and <see cref="StartedWorkflows"/>.</summary>
    public void Reset()
    {
        _executions.Clear();
        _startedWorkflows.Clear();
    }

    private SharedKernel.Primitives.Results.Result<IWorkflowHandle> StartCore<TWorkflow>(
        object? args, WorkflowStartOptions options, TenantScope tenantScope)
        where TWorkflow : WorkflowBase
    {
        ArgumentNullException.ThrowIfNull(options);

        // Fail-loud, before any state mutation or lookup -- mirrors the identical rule already
        // established for Search/InMemorySearchIndex and Intelligence/InMemoryVectorCollection, and is
        // MORE load-bearing here per the real IWorkflowDispatcher's own XML docs (a workflow id is a
        // flat, caller-addressable keyspace with no per-declaration tenant-field toggle to condition
        // the check on).
        if (tenantScope.IsGlobal)
        {
            return SharedKernel.Primitives.Results.Result<IWorkflowHandle>.Failure(WorkflowErrors.TenantScopeMissing());
        }

        if (SimulateFailure)
        {
            return SharedKernel.Primitives.Results.Result<IWorkflowHandle>.Failure(
                WorkflowErrors.ServiceUnavailable("SimulateFailure is enabled."));
        }

        if (string.IsNullOrWhiteSpace(options.BusinessKey))
        {
            return SharedKernel.Primitives.Results.Result<IWorkflowHandle>.Failure(
                WorkflowErrors.InvalidWorkflowId("BusinessKey must not be null, empty, or whitespace."));
        }

        var workflowTypeName = typeof(TWorkflow).Name;
        var workflowId = ComposeWorkflowId(workflowTypeName, options.BusinessKey, tenantScope);

        // At most one RUNNING execution per workflow id -- the fake's analogue of Temporal's own
        // durable-idempotency enforcement. WorkflowStartOptions.IdReusePolicy/.IdConflictPolicy's full
        // range (e.g. terminate-and-restart semantics) is a deliberate simplification deferred to a
        // future reconciliation pass against 17.Workflows's own shipped dispatcher behavior.
        if (_executions.TryGetValue(workflowId, out var existing) && existing.Status == WorkflowLifecycleStatus.Running)
        {
            return SharedKernel.Primitives.Results.Result<IWorkflowHandle>.Failure(WorkflowErrors.AlreadyStarted(workflowId));
        }

        var runId = string.Create(CultureInfo.InvariantCulture, $"in-memory-run-{Interlocked.Increment(ref _runIdSequence)}");
        var execution = new InMemoryWorkflowExecution
        {
            WorkflowId = workflowId,
            RunId = runId,
            TaskQueue = options.TaskQueue,
            TenantScope = tenantScope,
            WorkflowTypeName = workflowTypeName,
            Args = args,
        };

        _executions[workflowId] = execution;
        _startedWorkflows.Enqueue(new InMemoryWorkflowStartRecord(workflowTypeName, workflowId, tenantScope, args));

        return SharedKernel.Primitives.Results.Result<IWorkflowHandle>.Success(
            new InMemoryWorkflowHandle(_executions, workflowId, runId));
    }

    private string ComposeWorkflowId(string workflowTypeName, string businessKey, TenantScope tenantScope) =>
        _workflowIdFactory is not null
            ? _workflowIdFactory.Create(workflowTypeName, businessKey, tenantScope)
            : string.Join(WorkflowWellKnown.IdSeparator, tenantScope.ToString(), workflowTypeName, businessKey);

    private InMemoryWorkflowExecution RequireExecution(string workflowId) =>
        _executions.TryGetValue(workflowId, out var execution)
            ? execution
            : throw new InvalidOperationException(
                $"No workflow execution exists for id '{workflowId}'. Start it first via StartAsync.");

    private static WorkflowExecutionStatus MapStatus(WorkflowLifecycleStatus status) => status switch
    {
        WorkflowLifecycleStatus.Running => WorkflowExecutionStatus.Running,
        WorkflowLifecycleStatus.Completed => WorkflowExecutionStatus.Completed,
        WorkflowLifecycleStatus.Cancelled => WorkflowExecutionStatus.Canceled,
        WorkflowLifecycleStatus.Terminated => WorkflowExecutionStatus.Terminated,
        WorkflowLifecycleStatus.Failed => WorkflowExecutionStatus.Failed,
        _ => WorkflowExecutionStatus.Unspecified,
    };
}
