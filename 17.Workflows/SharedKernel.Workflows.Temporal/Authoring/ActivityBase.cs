using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Failures;
using SharedKernel.Workflows.Temporal.Interception;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Authoring;

/// <summary>
/// The base every platform Temporal activity extends. Activities are ordinary, DI-resolved code —
/// that is the entire point. Every platform rule that applies to ordinary service code applies here
/// unchanged, including the mandate to use <see cref="IClock"/> rather than
/// <see cref="DateTimeOffset.UtcNow"/>. The determinism rules that govern
/// <see cref="WorkflowBase"/> apply to workflows only and stop precisely at this boundary.
/// </summary>
public abstract class ActivityBase
{
    /// <summary>Initializes the activity base with ordinary, DI-resolved dependencies.</summary>
    /// <param name="logger">
    /// The activity's logger. A concrete activity's constructor typically accepts
    /// <c>ILogger&lt;TActivity&gt;</c>, which satisfies this parameter because
    /// <c>ILogger&lt;T&gt;</c> implements <see cref="ILogger"/>.
    /// </param>
    /// <param name="clock">The ordinary, DI-resolved clock. Correct and mandatory here, unlike inside a workflow.</param>
    protected ActivityBase(ILogger logger, IClock clock)
    {
        Logger = logger;
        Clock = clock;
    }

    /// <summary>Gets the activity's logger.</summary>
    protected ILogger Logger { get; }

    /// <summary>Gets the ordinary, DI-resolved clock.</summary>
    protected IClock Clock { get; }

    /// <summary>
    /// Gets the tenant scope propagated from the dispatching workflow's headers, or
    /// <see cref="TenantScope.None"/> if none was propagated.
    /// </summary>
    protected TenantScope TenantScope => ActivityPropagationContext.CurrentTenantScope;

    /// <summary>
    /// Records a heartbeat for the current activity invocation.
    /// </summary>
    /// <param name="details">Optional details recorded with the heartbeat, retrievable on retry.</param>
    /// <remarks>
    /// Heartbeating is not optional for long activities: an activity exceeding its heartbeat timeout
    /// without heartbeating is presumed dead and retried <em>while the original is still running</em>,
    /// producing a duplicate side effect. Any activity expected to run longer than
    /// <see cref="Constants.WorkflowWellKnown.DefaultHeartbeatTimeout"/> must heartbeat periodically.
    /// </remarks>
    protected void Heartbeat(params object?[] details)
    {
        ActivityExecutionContext.Current.Heartbeat(details);
        WorkflowLog.ActivityHeartbeatRecorded(Logger, GetType().Name);
    }

    /// <summary>
    /// Maps an expected <see cref="Error"/> to the <see cref="ApplicationFailureException"/> that
    /// should be thrown to report it to Temporal.
    /// </summary>
    protected static ApplicationFailureException Fail(Error error) => WorkflowFailureMapper.ToFailure(error);

    /// <summary>
    /// Maps a failed <see cref="Result"/> to the <see cref="ApplicationFailureException"/> that
    /// should be thrown to report it to Temporal.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="result"/> represents a success.</exception>
    protected static ApplicationFailureException FailFrom(Result result) => WorkflowFailureMapper.ToFailure(result);
}
