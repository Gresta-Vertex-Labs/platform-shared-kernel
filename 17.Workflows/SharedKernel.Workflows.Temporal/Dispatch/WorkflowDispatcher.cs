using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using SharedKernel.Workflows.Temporal.Interception;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// The default <see cref="IWorkflowDispatcher"/> — composes workflow ids through
/// <see cref="IWorkflowIdFactory"/> and dispatches through a real <see cref="ITemporalClient"/>.
/// </summary>
internal sealed class WorkflowDispatcher : IWorkflowDispatcher
{
    private readonly ITemporalClient _client;
    private readonly IWorkflowIdFactory _idFactory;
    private readonly ILogger<WorkflowDispatcher> _logger;

    public WorkflowDispatcher(ITemporalClient client, IWorkflowIdFactory idFactory, ILogger<WorkflowDispatcher> logger)
    {
        _client = client;
        _idFactory = idFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<IWorkflowHandle>> StartAsync<TWorkflow>(
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
        => StartCoreAsync<TWorkflow>(args: [], options, tenantScope, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IWorkflowHandle>> StartAsync<TWorkflow, TArgs>(
        TArgs args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
        => StartCoreAsync<TWorkflow>([args], options, tenantScope, cancellationToken);

    /// <inheritdoc />
    public async Task<Result<IWorkflowHandle<TResult>>> StartAsync<TWorkflow, TArgs, TResult>(
        TArgs args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
        where TWorkflow : WorkflowBase
    {
        Result<IWorkflowHandle> result = await StartCoreAsync<TWorkflow>([args], options, tenantScope, cancellationToken);
        if (result.IsFailure)
        {
            return Result<IWorkflowHandle<TResult>>.Failure(result.Error);
        }

        var adapter = (WorkflowHandleAdapter)result.Value;
        return Result<IWorkflowHandle<TResult>>.Success(new WorkflowHandleAdapter<TResult>(adapter.Handle, tenantScope));
    }

    private async Task<Result<IWorkflowHandle>> StartCoreAsync<TWorkflow>(
        object?[] args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
        where TWorkflow : WorkflowBase
    {
        // Fail-closed tenant guard — runs before the id is composed and before the client is touched.
        if (tenantScope == TenantScope.None)
        {
            WorkflowLog.TenantScopeMissingOnDispatch(_logger, typeof(TWorkflow).Name);
            return Result<IWorkflowHandle>.Failure(WorkflowErrors.TenantScopeMissing());
        }

        if (string.IsNullOrWhiteSpace(options.BusinessKey))
        {
            return Result<IWorkflowHandle>.Failure(WorkflowErrors.InvalidWorkflowId("business key must not be null or whitespace"));
        }

        string workflowTypeName = typeof(TWorkflow).Name;
        string workflowId = _idFactory.Create(workflowTypeName, options.BusinessKey, tenantScope);

        var workflowOptions = new WorkflowOptions(workflowId, options.TaskQueue)
        {
            IdReusePolicy = options.IdReusePolicy,
            IdConflictPolicy = options.IdConflictPolicy,
        };
        if (options.ExecutionTimeout is { } executionTimeout)
        {
            workflowOptions.ExecutionTimeout = executionTimeout;
        }

        if (options.RunTimeout is { } runTimeout)
        {
            workflowOptions.RunTimeout = runTimeout;
        }

        if (options.TaskTimeout is { } taskTimeout)
        {
            workflowOptions.TaskTimeout = taskTimeout;
        }

        if (options.RetryPolicy is { } retryPolicy)
        {
            workflowOptions.RetryPolicy = retryPolicy;
        }

        DispatchPropagationContext.SetTenantScope(tenantScope);
        try
        {
            WorkflowHandle handle = await _client.StartWorkflowAsync(workflowTypeName, args, workflowOptions);
            WorkflowLog.WorkflowStarted(_logger, workflowId, workflowTypeName);
            return Result<IWorkflowHandle>.Success(new WorkflowHandleAdapter(handle, tenantScope));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<IWorkflowHandle>.Failure(WorkflowFailureMapper.ToError(exception));
        }
    }

    /// <inheritdoc />
    public IWorkflowHandle GetHandle(string workflowId, string? runId, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (tenantScope == TenantScope.None)
        {
            throw new ArgumentException("A tenant scope is required to attach to a workflow handle.", nameof(tenantScope));
        }

        WorkflowHandle handle = _client.GetWorkflowHandle(workflowId, runId);
        return new WorkflowHandleAdapter(handle, tenantScope);
    }

    /// <inheritdoc />
    public IWorkflowHandle<TResult> GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        if (tenantScope == TenantScope.None)
        {
            throw new ArgumentException("A tenant scope is required to attach to a workflow handle.", nameof(tenantScope));
        }

        WorkflowHandle handle = _client.GetWorkflowHandle(workflowId, runId);
        return new WorkflowHandleAdapter<TResult>(handle, tenantScope);
    }

    /// <inheritdoc />
    public async Task<Result<WorkflowExecutionDescription>> DescribeAsync(
        string workflowId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        if (tenantScope == TenantScope.None)
        {
            return Result<WorkflowExecutionDescription>.Failure(WorkflowErrors.TenantScopeMissing());
        }

        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Result<WorkflowExecutionDescription>.Failure(WorkflowErrors.InvalidWorkflowId("workflow id must not be null or whitespace"));
        }

        DispatchPropagationContext.SetTenantScope(tenantScope);
        try
        {
            WorkflowHandle handle = _client.GetWorkflowHandle(workflowId);
            Temporalio.Client.WorkflowExecutionDescription description = await handle.DescribeAsync(
                new WorkflowDescribeOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });

            return Result<WorkflowExecutionDescription>.Success(new WorkflowExecutionDescription
            {
                Id = description.Id,
                RunId = description.RunId,
                WorkflowType = description.WorkflowType,
                TaskQueue = description.TaskQueue,
                Status = description.Status,
                StartTime = description.StartTime,
                CloseTime = description.CloseTime,
                HistoryLength = description.HistoryLength,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<WorkflowExecutionDescription>.Failure(WorkflowFailureMapper.ToError(exception));
        }
    }
}
