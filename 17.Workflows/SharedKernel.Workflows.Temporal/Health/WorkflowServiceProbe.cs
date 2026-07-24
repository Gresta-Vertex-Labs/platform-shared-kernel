using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;

namespace SharedKernel.Workflows.Temporal.Health;

/// <summary>
/// The default <see cref="IWorkflowServiceProbe"/> — checks Temporal service reachability, namespace
/// addressability, and (on a worker-hosting composition) whether this process's worker pollers are
/// still executing.
/// </summary>
internal sealed class WorkflowServiceProbe : IWorkflowServiceProbe
{
    private readonly ITemporalClient _client;
    private readonly TemporalOptions _options;
    private readonly IEnumerable<IHostedService> _hostedServices;
    private readonly ILogger<WorkflowServiceProbe> _logger;

    public WorkflowServiceProbe(
        ITemporalClient client,
        IOptions<TemporalOptions> options,
        IEnumerable<IHostedService> hostedServices,
        ILogger<WorkflowServiceProbe> logger)
    {
        _client = client;
        _options = options.Value;
        _hostedServices = hostedServices;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<WorkflowServiceHealth>> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        bool reachable;
        bool namespaceAddressable = false;

        try
        {
            reachable = await _client.Connection.CheckHealthAsync(
                _client.Connection.WorkflowService,
                new RpcOptions { CancellationToken = cancellationToken });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reachable = false;
        }

        if (reachable)
        {
            try
            {
                await _client.WorkflowService.DescribeNamespaceAsync(
                    new DescribeNamespaceRequest { Namespace = _options.Namespace },
                    new RpcOptions { CancellationToken = cancellationToken });
                namespaceAddressable = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                namespaceAddressable = false;
            }
        }

        stopwatch.Stop();
        bool workerPollersActive = DetermineWorkerPollersActive();

        if (!reachable || !namespaceAddressable || !workerPollersActive)
        {
            WorkflowLog.ProbeDegraded(_logger, reachable, namespaceAddressable, workerPollersActive);
        }

        return Result<WorkflowServiceHealth>.Success(new WorkflowServiceHealth
        {
            Reachable = reachable,
            NamespaceAddressable = namespaceAddressable,
            WorkerPollersActive = workerPollersActive,
            TaskQueueBacklog = null,
            Latency = stopwatch.Elapsed,
        });
    }

    /// <summary>
    /// On a client-only composition there are no worker pollers, so this always reports
    /// <see langword="true"/> — there is nothing to fail. On a worker-hosting composition, every
    /// registered <see cref="TemporalWorkerService"/>'s <c>ExecuteTask</c> must be running
    /// (non-null and not completed) for the pollers to be considered active.
    /// </summary>
    private bool DetermineWorkerPollersActive()
    {
        List<TemporalWorkerService> workers = _hostedServices.OfType<TemporalWorkerService>().ToList();
        return workers.Count == 0 || workers.All(worker => worker.ExecuteTask is { IsCompleted: false });
    }
}
