using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Diagnostics;
using SharedKernel.Workflows.Temporal.Interception;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// The default <see cref="IWorkflowHandle"/>: a thin, <see cref="Result"/>-returning wrapper over a real Temporal
/// <see cref="WorkflowHandle"/>. Only a <see cref="TemporalException"/> becomes a failed result; any other exception
/// propagates.
/// </summary>
internal class WorkflowHandleAdapter : IWorkflowHandle
{
    internal readonly WorkflowHandle Handle;
    private readonly TenantScope _tenantScope;
    private readonly ILogger _logger;

    public WorkflowHandleAdapter(WorkflowHandle handle, TenantScope tenantScope, ILogger logger)
    {
        Handle = handle;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    /// <inheritdoc />
    public string WorkflowId => Handle.Id;

    /// <inheritdoc />
    public string? RunId => Handle.RunId;

    /// <inheritdoc />
    public async Task<Result> SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken cancellationToken = default)
    {
        DispatchPropagationContext.SetTenantScope(_tenantScope);
        try
        {
            await Handle.SignalAsync(
                signalName,
                [args],
                new WorkflowSignalOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });
            WorkflowLog.SignalDispatched(_logger, signalName);
            return Result.Success();
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result.Failure(DispatchFailureReporter.Report(_logger, WorkflowOperations.Signal, exception));
        }
    }

    /// <inheritdoc />
    public async Task<Result<TQueryResult>> QueryAsync<TQueryResult>(string queryName, CancellationToken cancellationToken = default)
    {
        DispatchPropagationContext.SetTenantScope(_tenantScope);
        try
        {
            TQueryResult value = await Handle.QueryAsync<TQueryResult>(
                queryName,
                [],
                new WorkflowQueryOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });
            WorkflowLog.QueryDispatched(_logger, queryName);
            return Result<TQueryResult>.Success(value);
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result<TQueryResult>.Failure(DispatchFailureReporter.Report(_logger, WorkflowOperations.Query, exception));
        }
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(CancellationToken cancellationToken = default)
    {
        DispatchPropagationContext.SetTenantScope(_tenantScope);
        try
        {
            await Handle.CancelAsync(new WorkflowCancelOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });
            return Result.Success();
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result.Failure(DispatchFailureReporter.Report(_logger, WorkflowOperations.Cancel, exception));
        }
    }

    /// <inheritdoc />
    public async Task<Result> TerminateAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        DispatchPropagationContext.SetTenantScope(_tenantScope);
        try
        {
            await Handle.TerminateAsync(
                reason,
                new WorkflowTerminateOptions { Rpc = new RpcOptions { CancellationToken = cancellationToken } });
            WorkflowLog.WorkflowTerminated(_logger);
            return Result.Success();
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result.Failure(DispatchFailureReporter.Report(_logger, WorkflowOperations.Terminate, exception));
        }
    }
}

/// <summary>
/// The default <see cref="IWorkflowHandle{TResult}"/> — adds <see cref="GetResultAsync"/> over
/// <see cref="WorkflowHandleAdapter"/>.
/// </summary>
/// <typeparam name="TResult">The workflow's result type.</typeparam>
internal sealed class WorkflowHandleAdapter<TResult> : WorkflowHandleAdapter, IWorkflowHandle<TResult>
{
    private readonly ILogger _resultLogger;

    public WorkflowHandleAdapter(WorkflowHandle handle, TenantScope tenantScope, ILogger logger)
        : base(handle, tenantScope, logger)
    {
        _resultLogger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<TResult>> GetResultAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            TResult value = await Handle.GetResultAsync<TResult>(
                followRuns: true,
                rpcOptions: new RpcOptions { CancellationToken = cancellationToken });
            return Result<TResult>.Success(value);
        }
        catch (Exception exception) when (DispatchFailureReporter.CanHandle(exception))
        {
            return Result<TResult>.Failure(DispatchFailureReporter.Report(_resultLogger, WorkflowOperations.GetResult, exception));
        }
    }
}
