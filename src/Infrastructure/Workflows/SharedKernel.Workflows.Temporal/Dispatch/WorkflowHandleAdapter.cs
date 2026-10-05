using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Failures;
using SharedKernel.Workflows.Temporal.Interception;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// The default <see cref="IWorkflowHandle"/> — a thin, <see cref="Result"/>-returning wrapper over a
/// real Temporal <see cref="WorkflowHandle"/>.
/// </summary>
internal class WorkflowHandleAdapter : IWorkflowHandle
{
    internal readonly WorkflowHandle Handle;
    private readonly TenantScope _tenantScope;

    public WorkflowHandleAdapter(WorkflowHandle handle, TenantScope tenantScope)
    {
        Handle = handle;
        _tenantScope = tenantScope;
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
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(WorkflowFailureMapper.ToError(exception));
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
            return Result<TQueryResult>.Success(value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<TQueryResult>.Failure(WorkflowFailureMapper.ToError(exception));
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(WorkflowFailureMapper.ToError(exception));
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
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(WorkflowFailureMapper.ToError(exception));
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
    public WorkflowHandleAdapter(WorkflowHandle handle, TenantScope tenantScope)
        : base(handle, tenantScope)
    {
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<TResult>.Failure(WorkflowFailureMapper.ToError(exception));
        }
    }
}
