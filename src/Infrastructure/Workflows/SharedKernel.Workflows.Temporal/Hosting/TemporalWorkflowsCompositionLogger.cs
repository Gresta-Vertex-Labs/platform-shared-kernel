using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Workflows.Temporal.Diagnostics;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>Captures the shape of a single worker-hosting <see cref="TemporalWorkflowsBuilder.Build"/> call for startup logging.</summary>
internal sealed record TemporalWorkflowsCompositionSummary(string TaskQueue, int WorkflowCount, int ActivityCount);

/// <summary>
/// Logs the resolved <see cref="TemporalWorkflowsCompositionSummary"/> once at host startup.
/// </summary>
/// <remarks>
/// <see cref="ITemporalWorkflowsBuilder.Build"/> validates and registers services against an
/// <c>IServiceCollection</c>: no <see cref="ILogger"/> is resolvable at that point. This hosted
/// service defers the "worker built" log line to <see cref="StartAsync"/>, when a real
/// <see cref="ILogger"/> is available. A client-only composition registers no hosted service, so it logs its own
/// summary when the client is first built.
/// </remarks>
internal sealed class TemporalWorkflowsCompositionLogger : IHostedService
{
    private readonly ILogger<TemporalWorkflowsCompositionLogger> _logger;
    private readonly TemporalWorkflowsCompositionSummary _summary;

    public TemporalWorkflowsCompositionLogger(ILogger<TemporalWorkflowsCompositionLogger> logger, TemporalWorkflowsCompositionSummary summary)
    {
        _logger = logger;
        _summary = summary;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        WorkflowLog.WorkerBuilt(_logger, _summary.TaskQueue, _summary.WorkflowCount, _summary.ActivityCount);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
