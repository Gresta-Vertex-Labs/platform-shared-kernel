using SharedKernel.Primitives.Logging;

namespace SharedKernel.Caching.Redis.DistributedLocking.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Caching.Redis.DistributedLocking</c> sub-block of <c>02.Caching</c>: EventIds 2300-2399.
/// </summary>
internal static class DistributedLockingLoggingEventIds
{
    /// <summary>First id of the sub-block (2300).</summary>
    public const int Base = LoggingEventIdRanges.Caching + (3 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A lock was acquired.</summary>
    public const int LockAcquired = Base + 0;

    /// <summary>A lock is held by another owner.</summary>
    public const int LockContended = Base + 1;

    /// <summary>A lease was acquired.</summary>
    public const int LeaseAcquired = Base + 2;

    /// <summary>A lease is held by another owner.</summary>
    public const int LeaseContended = Base + 3;

    /// <summary>A lock was released.</summary>
    public const int LockReleased = Base + 4;

    /// <summary>Releasing a lock failed.</summary>
    public const int ReleaseFailed = Base + 5;

    /// <summary>Extending a lock failed.</summary>
    public const int ExtendFailed = Base + 6;

    /// <summary>A held lock was lost.</summary>
    public const int LockLost = Base + 7;
}
