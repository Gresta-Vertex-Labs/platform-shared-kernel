using SharedKernel.Primitives.Propagation;
using Temporalio.Common;

namespace SharedKernel.Workflows.Temporal.Constants;

/// <summary>
/// Well-known constants shared across the dispatch surface, authoring bases, propagation
/// interceptor, and worker hosting builder.
/// </summary>
public static class WorkflowWellKnown
{
    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> name used for this domain's diagnostics.
    /// </summary>
    /// <remarks>
    /// Named identically for the <c>13.ServiceDefaults</c> string-name-only
    /// <c>WithWorkflowTelemetry()</c> wiring to match without a <c>ProjectReference</c> to this
    /// package.
    /// </remarks>
    public const string ActivitySourceName = "SharedKernel.Workflows";

    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> name used for this domain's diagnostics.
    /// </summary>
    public const string MeterName = "SharedKernel.Workflows";

    /// <summary>
    /// The separator used to compose a tenant-scoped workflow id from its
    /// <c>{tenant}:{workflowType}:{businessKey}</c> segments.
    /// </summary>
    public const string IdSeparator = ":";

    /// <summary>
    /// The Temporal header key carrying the tenant id, forwarding to
    /// <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders.TenantId"/> — never an
    /// independently declared literal.
    /// </summary>
    public static readonly string TenantHeaderKey = WellKnownHeaders.TenantId;

    /// <summary>
    /// The Temporal header key carrying the correlation id, forwarding to
    /// <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders.CorrelationId"/> — never an
    /// independently declared literal.
    /// </summary>
    public static readonly string CorrelationHeaderKey = WellKnownHeaders.CorrelationId;

    /// <summary>
    /// The platform default <see cref="Temporalio.Workflows.ActivityOptions.StartToCloseTimeout"/>
    /// applied by <see cref="Authoring.WorkflowBase.ExecuteAsync{TActivity, TArgs, TResult}"/> when
    /// the caller does not override it.
    /// </summary>
    /// <remarks>
    /// Temporal's raw <c>ActivityOptions</c> has no default <c>StartToCloseTimeout</c> and rejects
    /// the call at runtime if neither it nor <c>ScheduleToCloseTimeout</c> is set — a first-run
    /// papercut every team hits exactly once. This constant is that default made explicit.
    /// </remarks>
    public static readonly TimeSpan DefaultStartToCloseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The platform default activity heartbeat timeout.
    /// </summary>
    public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The platform default maximum retry attempts for an activity's <see cref="RetryPolicy"/>.
    /// </summary>
    public const int DefaultMaximumAttempts = 5;
}
