namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// Named constants for every <c>Activity.SetTag</c>/metric-tag key this package emits — never a raw
/// string literal at a call site (mirrors the platform-wide magic-string convention, e.g.
/// <c>WellKnownTagKeys</c> in <c>01.Core</c>, applied locally since these tag keys are specific to this
/// domain's own telemetry rather than a cross-domain propagation concern).
/// </summary>
internal static class SchedulingTagKeys
{
    /// <summary>The registered job name.</summary>
    public const string JobName = "scheduling.job_name";

    /// <summary>The job's configured <see cref="Policies.MisfirePolicy"/>, as its enum name.</summary>
    public const string MisfirePolicy = "scheduling.misfire_policy";

    /// <summary>The job's configured <see cref="Policies.OverlapPolicy"/>, as its enum name.</summary>
    public const string OverlapPolicy = "scheduling.overlap_policy";

    /// <summary>The outcome of a single execution attempt (e.g. "succeeded", "failed", "threw").</summary>
    public const string Outcome = "scheduling.outcome";
}
