using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using SharedKernel.Workflows.Temporal.Interception;
using Temporalio.Client;
using Temporalio.Exceptions;

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
        return Result<IWorkflowHandle<TResult>>.Success(new WorkflowHandleAdapter<TResult>(adapter.Handle, tenantScope, _logger));
    }

    private async Task<Result<IWorkflowHandle>> StartCoreAsync<TWorkflow>(
        object?[] args,
        WorkflowStartOptions options,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
        where TWorkflow : WorkflowBase
    {
        // Fail-closed tenant guard: runs before the id is composed and before the client is touched.
        Result scope = CheckTenantScope(WorkflowOperations.Start, tenantScope);
        if (scope.IsFailure)
        {
            return Result<IWorkflowHandle>.Failure(scope.Error);
        }

        if (string.IsNullOrWhiteSpace(options.BusinessKey))
        {
            return Result<IWorkflowHandle>.Failure(
                Refuse(WorkflowOperations.Start, WorkflowErrors.InvalidWorkflowId("the business key must not be blank.")));
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
            WorkflowLog.WorkflowStarted(_logger, workflowTypeName);
            return Result<IWorkflowHandle>.Success(new WorkflowHandleAdapter(handle, tenantScope, _logger));
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result<IWorkflowHandle>.Failure(DispatchFailureReporter.Report(_logger, WorkflowOperations.Start, exception));
        }
    }

    /// <inheritdoc />
    public Result<IWorkflowHandle> GetHandle(string workflowId, string? runId, TenantScope tenantScope)
    {
        Result check = CheckHandleRequest(WorkflowOperations.GetHandle, workflowId, tenantScope);
        if (check.IsFailure)
        {
            return Result<IWorkflowHandle>.Failure(check.Error);
        }

        WorkflowHandle handle = _client.GetWorkflowHandle(workflowId, runId);
        return Result<IWorkflowHandle>.Success(new WorkflowHandleAdapter(handle, tenantScope, _logger));
    }

    /// <inheritdoc />
    public Result<IWorkflowHandle<TResult>> GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope)
    {
        Result check = CheckHandleRequest(WorkflowOperations.GetHandle, workflowId, tenantScope);
        if (check.IsFailure)
        {
            return Result<IWorkflowHandle<TResult>>.Failure(check.Error);
        }

        WorkflowHandle handle = _client.GetWorkflowHandle(workflowId, runId);
        return Result<IWorkflowHandle<TResult>>.Success(new WorkflowHandleAdapter<TResult>(handle, tenantScope, _logger));
    }

    /// <inheritdoc />
    public async Task<Result<WorkflowExecutionDescription>> DescribeAsync(
        string workflowId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        Result check = CheckHandleRequest(WorkflowOperations.Describe, workflowId, tenantScope);
        if (check.IsFailure)
        {
            return Result<WorkflowExecutionDescription>.Failure(check.Error);
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
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result<WorkflowExecutionDescription>.Failure(
                DispatchFailureReporter.Report(_logger, WorkflowOperations.Describe, exception));
        }
    }

    /// <summary>
    /// The one guard shared by every member that addresses an existing execution by id: a global scope is refused
    /// (<c>workflow.tenant_scope_missing</c>), a blank id is invalid, and an id whose tenant segment is not the
    /// caller's is answered exactly like a missing execution (<c>workflow.not_found</c>), so the answer is no
    /// existence oracle for another tenant's ids.
    /// </summary>
    private Result CheckHandleRequest(string operation, string workflowId, TenantScope tenantScope)
    {
        Result scope = CheckTenantScope(operation, tenantScope);
        if (scope.IsFailure)
        {
            return scope;
        }

        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return Result.Failure(Refuse(operation, WorkflowErrors.InvalidWorkflowId("the workflow id must not be blank.")));
        }

        return WorkflowIdOwnership.IsOwnedBy(workflowId, tenantScope)
            ? Result.Success()
            : Result.Failure(Refuse(operation, WorkflowErrors.NotFound()));
    }

    private Result CheckTenantScope(string operation, TenantScope tenantScope)
    {
        if (!tenantScope.IsGlobal)
        {
            return Result.Success();
        }

        WorkflowLog.TenantScopeMissingOnDispatch(_logger, operation);
        return Result.Failure(WorkflowErrors.TenantScopeMissing());
    }

    private Error Refuse(string operation, Error error)
    {
        WorkflowLog.DispatchFailed(_logger, operation, error.Code, error.Type);
        return error;
    }
}
