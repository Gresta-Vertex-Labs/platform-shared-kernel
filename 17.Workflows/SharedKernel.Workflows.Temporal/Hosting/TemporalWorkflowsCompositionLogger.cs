using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Workflows.Temporal.Logging;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>Captures the shape of a single <see cref="TemporalWorkflowsBuilder.Build"/> call for startup logging.</summary>
internal sealed record TemporalWorkflowsCompositionSummary(bool ClientOnly, string? TaskQueue, int WorkflowCount, int ActivityCount);

/// <summary>
/// Logs the resolved <see cref="TemporalWorkflowsCompositionSummary"/> once at host startup.
/// </summary>
/// <remarks>
/// <see cref="ITemporalWorkflowsBuilder.Build"/> validates and registers services against an
/// <c>IServiceCollection</c> — no <see cref="ILogger"/> is resolvable at that point. This hosted
/// service defers the "composition built" log line to <see cref="StartAsync"/>, when a real
/// <see cref="ILogger"/> is available.
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
        if (_summary.ClientOnly)
        {
            WorkflowLog.ClientOnlyBuilt(_logger);
        }
        else
        {
            WorkflowLog.WorkerBuilt(_logger, _summary.TaskQueue!, _summary.WorkflowCount, _summary.ActivityCount);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
