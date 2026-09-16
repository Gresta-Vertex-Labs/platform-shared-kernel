using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Workflows.Temporal.Authoring;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelTemporalWorkflows(configuration)</c>.
/// </summary>
/// <remarks>
/// <see cref="Build"/> validates the composition eagerly: a worker registered with zero workflows
/// and zero activities, a task queue registered twice, a non-<c>[Workflow]</c> type passed to
/// <see cref="AddWorkflow{TWorkflow}"/>, or <see cref="WithPayloadEncryption"/> with no configured
/// key each fail at <see cref="Build"/> with a named error — never as a silently idle worker polling
/// an empty task queue.
/// </remarks>
public interface ITemporalWorkflowsBuilder
{
    /// <summary>Registers a workflow type with the hosted worker.</summary>
    /// <typeparam name="TWorkflow">The workflow type. Must be a real Temporal <c>[Workflow]</c>-attributed type.</typeparam>
    ITemporalWorkflowsBuilder AddWorkflow<TWorkflow>()
        where TWorkflow : WorkflowBase;

    /// <summary>Registers an activities class with the hosted worker, resolved scoped per activity task.</summary>
    /// <typeparam name="TActivities">The activities class.</typeparam>
    ITemporalWorkflowsBuilder AddActivities<TActivities>()
        where TActivities : class;

    /// <summary>
    /// Configures this composition to host a worker on <paramref name="taskQueue"/>.
    /// </summary>
    /// <param name="taskQueue">The Temporal task queue this worker polls.</param>
    /// <param name="tune">
    /// An optional transform applied to <see cref="WorkerTuningOptions.Default"/>, e.g.
    /// <c>tune => tune with { MaxConcurrentActivities = 50 }</c>.
    /// </param>
    ITemporalWorkflowsBuilder WithWorker(string taskQueue, Func<WorkerTuningOptions, WorkerTuningOptions>? tune = null);

    /// <summary>
    /// Configures this composition as dispatch-only — no worker is hosted. The most common
    /// registration in a microservice fleet: a service that starts and signals workflows but hosts
    /// no workflow code needs a client and nothing else.
    /// </summary>
    /// <remarks>
    /// Calling <see cref="AddWorkflow{TWorkflow}"/>, <see cref="AddActivities{TActivities}"/>, or
    /// <see cref="WithWorker"/> after this is a configuration error caught at <see cref="Build"/>.
    /// </remarks>
    ITemporalWorkflowsBuilder AsClientOnly();

    /// <summary>
    /// Enables AES-256-GCM payload encryption for every workflow input, output, signal payload, and
    /// activity argument, via <c>01.Core</c>'s <c>ISymmetricEncryptionService</c>.
    /// </summary>
    /// <remarks>
    /// Payloads are encrypted with the key provider's current key; each payload records its key id, so
    /// rotation needs no configuration here. Requires an <c>ISymmetricEncryptionService</c> registration:
    /// register an <c>IEncryptionKeyProvider</c> and call
    /// <c>AddSharedKernelCryptography(configuration).AddSymmetricEncryption()</c>. Without it, resolving the
    /// Temporal client fails.
    /// </remarks>
    ITemporalWorkflowsBuilder WithPayloadEncryption();

    /// <summary>Enables distributed tracing via Temporal's <c>TracingInterceptor</c>.</summary>
    ITemporalWorkflowsBuilder WithOpenTelemetry();

    /// <summary>Enables SDK-core metrics via a <c>CustomMetricMeter</c> bridged onto this domain's <c>Meter</c>.</summary>
    ITemporalWorkflowsBuilder WithMetrics();

    /// <summary>
    /// Registers <c>ITemporalRawClientAccessor</c>, the last-resort raw-client escape hatch.
    /// </summary>
    /// <remarks>
    /// <b>THE RAW CLIENT BYPASSES TENANT SCOPING AND WORKFLOW-ID COMPOSITION.</b> Logs a startup
    /// <c>Warning</c> (EventId 17012). Use only for genuine last-resort capabilities this package
    /// does not model (Visibility API queries, schedules, namespace administration, Nexus operations).
    /// </remarks>
    ITemporalWorkflowsBuilder AllowRawClientAccess();

    /// <summary>Validates the composition and returns the underlying <see cref="IServiceCollection"/>.</summary>
    /// <exception cref="InvalidOperationException">The composition is invalid — see the type-level remarks.</exception>
    IServiceCollection Build();
}
