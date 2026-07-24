using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Activities;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// Sample activities shared across the real-<c>WorkflowEnvironment</c> test suite (T-09..T-16). Every
/// activity carries its own explicitly-named <c>[Activity]</c> entry point, matching the verified
/// Core-phase finding that Temporal names an attributed method by its own method name — never the
/// declaring type name — when no explicit name is supplied.
/// </summary>
public sealed class EchoActivity : ActivityBase
{
    public EchoActivity(ILogger<EchoActivity> logger, IClock clock) : base(logger, clock)
    {
    }

    [Activity(nameof(EchoActivity))]
    public Task<string> RunAsync(string input) => Task.FromResult($"echo:{input}");
}

/// <summary>Always fails with a retryable (<see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/>) error — used to exhaust a small retry policy (T-10).</summary>
public sealed class FlakyActivity : ActivityBase
{
    public FlakyActivity(ILogger<FlakyActivity> logger, IClock clock) : base(logger, clock)
    {
    }

    [Activity(nameof(FlakyActivity))]
    public Task<string> RunAsync(string input) => throw Fail(WorkflowErrors.ServiceUnavailable("simulated flaky failure"));
}

/// <summary>Never completes on its own — used to exercise a <c>ScheduleToCloseTimeout</c> (T-10).</summary>
public sealed class NeverCompletingActivity : ActivityBase
{
    public NeverCompletingActivity(ILogger<NeverCompletingActivity> logger, IClock clock) : base(logger, clock)
    {
    }

    [Activity(nameof(NeverCompletingActivity))]
    public async Task<string> RunAsync(string input)
    {
        // Bounded rather than Timeout.InfiniteTimeSpan: the workflow's ScheduleToCloseTimeout should
        // fail the WORKFLOW well before this elapses, but bounding it here guarantees the underlying
        // .NET task always completes on its own — even if activity-side cancellation propagation ever
        // has a hiccup — so worker/test teardown (graceful shutdown) never hangs waiting on it.
        await Task.Delay(TimeSpan.FromSeconds(10), ActivityExecutionContext.Current.CancellationToken);
        return input;
    }
}

/// <summary>
/// Records that it ran, keyed by the workflow's own compensation key, so a real-environment test can
/// observe that a cancelled workflow's compensation path genuinely executed (T-11).
/// </summary>
public sealed class CompensationActivity : ActivityBase
{
    /// <summary>Every compensation key ever recorded, for test assertion. Test-only shared state — not production code.</summary>
    public static readonly ConcurrentDictionary<string, bool> Invocations = new();

    public CompensationActivity(ILogger<CompensationActivity> logger, IClock clock) : base(logger, clock)
    {
    }

    [Activity(nameof(CompensationActivity))]
    public Task<string> RunAsync(string compensationKey)
    {
        Invocations[compensationKey] = true;
        return Task.FromResult(compensationKey);
    }
}

/// <summary>Returns the tenant scope this activity observed, for propagation assertions (T-15).</summary>
public sealed class PropagationActivity : ActivityBase
{
    public PropagationActivity(ILogger<PropagationActivity> logger, IClock clock) : base(logger, clock)
    {
    }

    [Activity(nameof(PropagationActivity))]
    public Task<string> RunAsync(string input) => Task.FromResult(TenantScope.Value);
}
