using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The <c>SharedKernel.Persistence.EfCore</c> sub-block of <c>06.Persistence</c>: EventIds 6000-6099.
/// </summary>
/// <remarks>
/// 6007 (a retired retry listener), 6009 (a retired connection reset), 6011 and 6012 are free and are not reused: a
/// number that once reached a dashboard keeps its meaning or stays empty.
/// </remarks>
internal static class EfCoreLoggingEventIds
{
    /// <summary>First id of the sub-block (6000).</summary>
    public const int Base = LoggingEventIdRanges.Persistence + (0 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A concurrency conflict was translated into a <c>ConflictException</c>.</summary>
    public const int ConcurrencyConflictDetected = Base + 0;

    /// <summary>The startup migration and seed sequence started.</summary>
    public const int MigrationAndSeedStarted = Base + 1;

    /// <summary>A seeder was applied.</summary>
    public const int SeederApplied = Base + 2;

    /// <summary>The startup migration and seed sequence completed.</summary>
    public const int MigrationAndSeedCompleted = Base + 3;

    /// <summary>The startup migration and seed sequence failed.</summary>
    public const int MigrationAndSeedFailed = Base + 4;

    /// <summary>The startup advisory lock was acquired.</summary>
    public const int AdvisoryLockAcquired = Base + 5;

    /// <summary>The startup advisory lock was released.</summary>
    public const int AdvisoryLockReleased = Base + 6;

    /// <summary>A retrying execution strategy gave up.</summary>
    public const int TransientRetryExhausted = Base + 8;

    /// <summary>A failed save was classified by its SQLSTATE.</summary>
    public const int DatabaseErrorClassified = Base + 10;

    /// <summary>Startup migrations or seeders are configured but no migration lock is registered.</summary>
    public const int NoMigrationLockRegistered = Base + 13;

    /// <summary>Domain events were discarded at save time.</summary>
    public const int DomainEventsDiscarded = Base + 14;

    /// <summary>A write to a row of another tenant was rejected.</summary>
    public const int TenantIsolationViolationProven = Base + 15;

    /// <summary>The context can raise domain events but no dispatcher is registered.</summary>
    public const int NoDomainEventDispatcherRegistered = Base + 16;

    /// <summary>The current row could not be read after a concurrency conflict.</summary>
    public const int ConflictRowLookupFailed = Base + 17;

    /// <summary>The model of a context was validated at startup.</summary>
    public const int ModelValidated = Base + 18;

    /// <summary>A context cannot join the transaction of the unit of work.</summary>
    public const int ContextCannotJoinTransaction = Base + 20;

    /// <summary>The transaction was rolled back because joined work failed.</summary>
    public const int RolledBackAfterJoinedFailure = Base + 21;

    /// <summary>An expected entity version was treated as stale.</summary>
    public const int EntityVersionRejected = Base + 22;

    /// <summary>The entity version key could not be refreshed.</summary>
    public const int EntityVersionKeyRefreshFailed = Base + 23;

    /// <summary>The current version of a conflicting row could not be sealed.</summary>
    public const int EntityVersionNotSealed = Base + 24;

    /// <summary>The startup warm-up of the entity version key failed.</summary>
    public const int EntityVersionKeyWarmUpFailed = Base + 25;

    /// <summary>The startup warm-up of the entity version key timed out.</summary>
    public const int EntityVersionKeyWarmUpTimedOut = Base + 26;

    /// <summary>A context moved onto the cross-tenant database role (was 6350).</summary>
    public const int CrossTenantConnectionUsed = Base + 27;

    /// <summary>The startup coverage check found a tenant table row-level security does not protect (was 6351).</summary>
    public const int TenantTableNotProtected = Base + 28;

    /// <summary>The model of a context failed validation at startup.</summary>
    public const int ModelValidationFailed = Base + 29;
}
