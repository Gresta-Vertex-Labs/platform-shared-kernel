using SharedKernel.Primitives.Logging;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// This package's <c>EventId</c>s: sub-block 0 (<c>19000</c>-<c>19099</c>) of <c>19.Scheduling</c>'s range
/// (<see cref="LoggingEventIdRanges.Scheduling"/>), as recorded in the domain's ownership table. The
/// package is the only one in its domain, so no other sub-block exists.
/// </summary>
internal static class SchedulingLoggingEventIds
{
    public const int Base = LoggingEventIdRanges.Scheduling + (0 * LoggingEventIdRanges.PackageSubBlockWidth);

    public const int HostedServiceStarted = Base + 0;
    public const int HostedServiceStopping = Base + 1;
    public const int SingleReplicaWarning = Base + 2;
    public const int JobFireStarted = Base + 3;
    public const int JobFireSucceeded = Base + 4;
    public const int JobFireFailed = Base + 5;
    public const int JobFireThrew = Base + 6;
    public const int JobSkippedOverlap = Base + 7;
    public const int JobQueuedOverlap = Base + 8;
    public const int MisfireDetected = Base + 9;
    public const int MisfireSkipped = Base + 10;
    public const int LockAcquisitionFailed = Base + 11;
    public const int LockAcquired = Base + 12;
    public const int HostedServiceFaulted = Base + 13;
    public const int JobCancelled = Base + 14;
    public const int ShutdownDrainTimedOut = Base + 15;
    public const int LockStoreUnavailable = Base + 16;
    public const int ShutdownDrainFaulted = Base + 17;
}
