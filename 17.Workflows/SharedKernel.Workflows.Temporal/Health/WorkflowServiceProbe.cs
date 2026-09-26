using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Health;
using SharedKernel.Workflows.Temporal.Configuration;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;

namespace SharedKernel.Workflows.Temporal.Health;

/// <summary>
/// The workflow readiness probe (<see cref="WorkflowReadiness.ProbeName"/>) — checks Temporal service reachability,
/// namespace addressability, and (on a worker-hosting composition) whether this process's worker pollers are still
/// executing.
/// </summary>
/// <remarks>
/// The Temporal client and the hosted services are resolved on the first probe, never in the constructor: a host
/// constructs every probe to read its name while it builds its health checks, which can happen while hosted services
/// are themselves being created — resolving them here would be a circular dependency.
/// </remarks>
internal sealed class WorkflowServiceProbe(IServiceProvider services) : IReadinessProbe
{
    public string Name => WorkflowReadiness.ProbeName;

    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = services.GetRequiredService<ITemporalClient>();
        var options = services.GetRequiredService<IOptions<TemporalOptions>>().Value;
        var logger = services.GetRequiredService<ILogger<WorkflowServiceProbe>>();

        var stopwatch = Stopwatch.StartNew();
        bool reachable;
        bool namespaceAddressable = false;

        try
        {
            reachable = await client.Connection.CheckHealthAsync(
                client.Connection.WorkflowService,
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
                await client.WorkflowService.DescribeNamespaceAsync(
                    new DescribeNamespaceRequest { Namespace = options.Namespace },
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

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [WorkflowReadiness.ReachableKey] = reachable,
            [WorkflowReadiness.NamespaceAddressableKey] = namespaceAddressable,
            [WorkflowReadiness.WorkerPollersActiveKey] = workerPollersActive,
        };

        if (reachable && namespaceAddressable && workerPollersActive)
        {
            return ReadinessReport.Healthy(
                "Workflow service reachable, namespace addressable, and worker pollers active.", data, stopwatch.Elapsed);
        }

        WorkflowLog.ProbeDegraded(logger, reachable, namespaceAddressable, workerPollersActive);
        return ReadinessReport.Unhealthy(
            $"Workflow service not ready (Reachable={reachable}, NamespaceAddressable={namespaceAddressable}, "
            + $"WorkerPollersActive={workerPollersActive}).",
            data,
            stopwatch.Elapsed);
    }

    private bool DetermineWorkerPollersActive()
    {
        List<TemporalWorkerService> workers = services.GetServices<IHostedService>().OfType<TemporalWorkerService>().ToList();
        return workers.Count == 0 || workers.All(worker => worker.ExecuteTask is { IsCompleted: false });
    }
}
