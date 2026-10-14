namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// Named constants for every package-specific <c>Activity.SetTag</c> and metric-tag key this package emits,
/// never a raw string literal at a call site. The cross-cutting <c>outcome</c> key is
/// <c>WellKnownTagKeys.Outcome</c>.
/// </summary>
internal static class SchedulingTagKeys
{
    /// <summary>The registered job name.</summary>
    public const string JobName = "scheduling.job_name";

    /// <summary>The job's configured <see cref="Policies.MisfirePolicy"/>, as its enum name.</summary>
    public const string MisfirePolicy = "scheduling.misfire_policy";

    /// <summary>The job's configured <see cref="Policies.OverlapPolicy"/>, as its enum name.</summary>
    public const string OverlapPolicy = "scheduling.overlap_policy";

    /// <summary>The result of the cross-replica occurrence claim, one of <see cref="SchedulingClaimResults"/>.</summary>
    public const string Claim = "scheduling.claim";
}
