using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.EfCore.Encryption</c> sub-block of <c>06.Persistence</c>: EventIds 6500-6599.
/// </summary>
internal static class EncryptionLoggingEventIds
{
    /// <summary>First id of the sub-block (6500).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (5 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>Refreshing the keys from the key provider failed.</summary>
    public const int KeyRefreshFailed = Base + 0;

    /// <summary>The keys have not been refreshed for too long.</summary>
    public const int KeysStale = Base + 1;

    /// <summary>A key that was not loaded was fetched after a value needed it.</summary>
    public const int KeyFetchedOnMiss = Base + 2;

    /// <summary>Fetching a key after a value needed it failed.</summary>
    public const int KeyFetchOnMissFailed = Base + 3;

    /// <summary>A save stopped between encryption and restore.</summary>
    public const int StalePendingRestored = Base + 4;

    /// <summary>A maintenance target started.</summary>
    public const int MaintenanceTargetStarted = Base + 10;

    /// <summary>A maintenance batch was processed.</summary>
    public const int MaintenanceBatchProcessed = Base + 11;

    /// <summary>A maintenance run finished.</summary>
    public const int MaintenanceCompleted = Base + 12;

    /// <summary>The maintenance job skipped values it could not process.</summary>
    public const int MaintenanceValuesSkipped = Base + 13;

    /// <summary>A tenant data key was created.</summary>
    public const int TenantKeyCreated = Base + 20;

    /// <summary>A tenant data key was shredded.</summary>
    public const int TenantKeyShredded = Base + 21;

    /// <summary>A shredded tenant still has values that are not erased.</summary>
    public const int TenantShredIncomplete = Base + 22;
}
