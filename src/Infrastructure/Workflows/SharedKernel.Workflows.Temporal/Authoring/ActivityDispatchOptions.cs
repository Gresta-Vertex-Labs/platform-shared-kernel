using Temporalio.Common;

namespace SharedKernel.Workflows.Temporal.Authoring;

/// <summary>
/// Options controlling how <see cref="WorkflowBase.ExecuteAsync{TActivity, TArgs, TResult}"/>
/// dispatches an activity call.
/// </summary>
/// <remarks>
/// Temporal's raw activity options carry no default <see cref="StartToCloseTimeout"/> and reject the
/// call at runtime if neither it nor <see cref="ScheduleToCloseTimeout"/> is set. Use
/// <see cref="Default"/> (or leave the parameter unset) to receive the platform default from
/// <see cref="Constants.WorkflowWellKnown.DefaultStartToCloseTimeout"/>.
/// </remarks>
public sealed record ActivityDispatchOptions
{
    /// <summary>Gets the default options — the platform's default timeout and retry policy.</summary>
    public static ActivityDispatchOptions Default { get; } = new();

    /// <summary>Gets the maximum time a single activity attempt may take, start to close.</summary>
    public TimeSpan? StartToCloseTimeout { get; init; }

    /// <summary>Gets the maximum time the activity may take across all retries, from schedule to close.</summary>
    public TimeSpan? ScheduleToCloseTimeout { get; init; }

    /// <summary>Gets the maximum time the activity may wait to start after being scheduled.</summary>
    public TimeSpan? ScheduleToStartTimeout { get; init; }

    /// <summary>
    /// Gets the maximum time allowed between heartbeats. An activity expected to run longer than
    /// this MUST heartbeat, or it is presumed dead and retried while the original is still running,
    /// producing a duplicate side effect.
    /// </summary>
    public TimeSpan? HeartbeatTimeout { get; init; }

    /// <summary>Gets the retry policy applied to the activity, if any.</summary>
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>
    /// Gets the cancellation token this activity call observes. Left <see langword="null"/> (the
    /// default), the call observes the ambient <c>Workflow.CancellationToken</c> — the ordinary case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set this to <see cref="System.Threading.CancellationToken.None"/> for a compensation
    /// activity dispatched from a <c>catch</c> block that runs after the workflow's own cancellation
    /// was observed.</b> Verified empirically at Tests phase (T-11) against the real Temporalio 1.17.0
    /// assembly: both <c>Temporalio.Workflows.ActivityOptions.CancellationToken</c> and
    /// <c>DelayOptions.CancellationToken</c> default to the ambient <c>Workflow.CancellationToken</c>
    /// when left unset — including for a call issued <em>after</em> that token has already been
    /// cancelled. Without this override, a compensation activity dispatched inside a
    /// <c>catch (CanceledFailureException)</c> block is itself immediately cancelled before it can
    /// run, silently defeating the "cancel runs the compensation path" guarantee
    /// <see cref="Dispatch.IWorkflowHandle.CancelAsync"/>'s XML doc promises.
    /// </para>
    /// </remarks>
    public CancellationToken? CancellationToken { get; init; }
}
