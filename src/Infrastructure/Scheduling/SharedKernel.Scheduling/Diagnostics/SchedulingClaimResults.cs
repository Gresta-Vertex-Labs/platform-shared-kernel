namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// The values of the <see cref="SchedulingTagKeys.Claim"/> span tag. A claim lost to another replica is
/// neither a success nor a failure, so it cannot be an <c>outcome</c> value.
/// </summary>
internal static class SchedulingClaimResults
{
    /// <summary>This replica holds the occurrence's lease.</summary>
    public const string Acquired = "acquired";

    /// <summary>Another replica already holds the occurrence's lease.</summary>
    public const string NotAcquired = "not_acquired";

    /// <summary>The lock store could not be reached, so no replica can prove ownership.</summary>
    public const string StoreUnavailable = "store_unavailable";
}
