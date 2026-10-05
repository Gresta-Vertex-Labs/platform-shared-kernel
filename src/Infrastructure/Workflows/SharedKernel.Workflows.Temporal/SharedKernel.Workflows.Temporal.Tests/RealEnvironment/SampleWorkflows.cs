using SharedKernel.Workflows.Temporal.Authoring;
using Temporalio.Common;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>Round trip: start, execute one activity, await the result (T-09, T-13, T-16).</summary>
[Workflow]
public sealed class EchoWorkflow : WorkflowBase
{
    [WorkflowRun]
    public Task<string> RunAsync(string input) => ExecuteAsync<EchoActivity, string, string>(input);
}

/// <summary>
/// Deliberately non-deterministic variant of <see cref="EchoWorkflow"/>, registered under the SAME
/// Temporal workflow type name, calling the activity twice instead of once. Used only inside a
/// standalone <c>WorkflowReplayer</c> (T-13) to prove the replay-determinism test has teeth — never
/// registered on the shared worker.
/// </summary>
[Workflow(nameof(EchoWorkflow))]
public sealed class BadEchoWorkflowVariant : WorkflowBase
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        await ExecuteAsync<EchoActivity, string, string>(input);
        return await ExecuteAsync<EchoActivity, string, string>(input);
    }
}

/// <summary>Awaits a 30-day timer — proves genuine time-skipping under <c>WorkflowEnvironment</c> (T-10).</summary>
[Workflow]
public sealed class DelayWorkflow : WorkflowBase
{
    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Workflow.DelayAsync(TimeSpan.FromDays(30));
        return "delayed-done";
    }
}

/// <summary>Races a long timer against a signal (T-10).</summary>
[Workflow]
public sealed class TimerVsSignalWorkflow : WorkflowBase
{
    private bool _signalled;

    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        Task timerTask = Workflow.DelayAsync(TimeSpan.FromDays(1));
        Task signalTask = Workflow.WaitConditionAsync(() => _signalled);
        Task completed = await Task.WhenAny(timerTask, signalTask);
        return ReferenceEquals(completed, timerTask) ? "timer" : "signal";
    }

    [WorkflowSignal]
    public Task Signal(string reason)
    {
        _signalled = true;
        return Task.CompletedTask;
    }
}

/// <summary>Calls an always-failing activity with a small retry policy so it genuinely exhausts (T-10).</summary>
[Workflow]
public sealed class RetryExhaustionWorkflow : WorkflowBase
{
    [WorkflowRun]
    public Task<string> RunAsync(string input) => ExecuteAsync<FlakyActivity, string, string>(
        input,
        new ActivityDispatchOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(5),
            RetryPolicy = new RetryPolicy { MaximumAttempts = 2, InitialInterval = TimeSpan.FromMilliseconds(50) },
        });
}

/// <summary>Calls a never-completing activity with a short <c>ScheduleToCloseTimeout</c> (T-10).</summary>
[Workflow]
public sealed class ScheduleToCloseTimeoutWorkflow : WorkflowBase
{
    [WorkflowRun]
    public Task<string> RunAsync(string input) => ExecuteAsync<NeverCompletingActivity, string, string>(
        input,
        new ActivityDispatchOptions
        {
            ScheduleToCloseTimeout = TimeSpan.FromSeconds(2),
            RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
        });
}

/// <summary>
/// Signal/query proof plus cancel-with-compensation proof (T-11): blocks on a long timer; on
/// cooperative cancellation, runs a compensation activity before completing as cancelled.
/// </summary>
[Workflow]
public sealed class CompensatingWorkflow : WorkflowBase
{
    private string _message = string.Empty;

    [WorkflowRun]
    public async Task<string> RunAsync(string compensationKey)
    {
        try
        {
            // Passed explicitly (rather than relying on any implicit default) — Workflow.DelayAsync
            // only observes cooperative cancellation for the token it was actually given.
            await Workflow.DelayAsync(TimeSpan.FromDays(1), Workflow.CancellationToken);
            return "completed-normally";
        }
        // Verified empirically at Tests phase (T-11): Workflow.DelayAsync surfaces a cancelled
        // Workflow.CancellationToken as Temporalio.Exceptions.CanceledFailureException, NOT the BCL
        // OperationCanceledException (CanceledFailureException does not derive from it) — a workflow
        // meaning to run compensation on cancellation must catch the Temporal type explicitly.
        catch (Exception ex) when (ex is OperationCanceledException or Temporalio.Exceptions.CanceledFailureException)
        {
            // CancellationToken.None is REQUIRED here — also verified empirically at Tests phase
            // (T-11): an activity call left to default to the ambient Workflow.CancellationToken is
            // immediately cancelled too once that token has already been cancelled, silently
            // preventing any compensation activity from ever running. See
            // ActivityDispatchOptions.CancellationToken's remarks for the full finding.
            await ExecuteAsync<CompensationActivity, string, string>(
                compensationKey,
                new ActivityDispatchOptions { CancellationToken = CancellationToken.None });
            throw;
        }
    }

    [WorkflowSignal]
    public Task SetMessage(string message)
    {
        _message = message;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public string GetMessage() => _message;
}

/// <summary>A snapshot of the tenant scope/correlation id/activity-observed tenant scope a workflow observed.</summary>
public sealed record PropagationSnapshot(string TenantScope, string CorrelationId, string ActivityTenantScope);

/// <summary>The parent's own snapshot, plus the child workflow's snapshot observed across the hop.</summary>
public sealed record PropagationResult(string TenantScope, string CorrelationId, string ActivityTenantScope, PropagationSnapshot Child);

/// <summary>Child workflow started by <see cref="PropagationParentWorkflow"/> — proves header propagation across a child-workflow hop (T-15).</summary>
[Workflow]
public sealed class PropagationChildWorkflow : WorkflowBase
{
    [WorkflowRun]
    public async Task<PropagationSnapshot> RunAsync(string input)
    {
        string activityTenant = await ExecuteAsync<PropagationActivity, string, string>(input);
        return new PropagationSnapshot(TenantScope.ToString(), CorrelationId, activityTenant);
    }
}

/// <summary>Parent workflow observing its own propagated headers, an activity's, and a child workflow's (T-15).</summary>
[Workflow]
public sealed class PropagationParentWorkflow : WorkflowBase
{
    [WorkflowRun]
    public async Task<PropagationResult> RunAsync(string input)
    {
        string activityTenant = await ExecuteAsync<PropagationActivity, string, string>(input);
        PropagationSnapshot child = await Workflow.ExecuteChildWorkflowAsync<PropagationSnapshot>(
            nameof(PropagationChildWorkflow),
            [input],
            new ChildWorkflowOptions { Id = $"{Workflow.Info.WorkflowId}-child" });

        return new PropagationResult(TenantScope.ToString(), CorrelationId, activityTenant, child);
    }
}
