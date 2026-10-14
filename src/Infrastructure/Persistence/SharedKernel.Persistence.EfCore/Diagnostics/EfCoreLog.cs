using Microsoft.Extensions.Logging;
using SharedKernel.Persistence.EfCore.Concurrency;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Persistence.EfCore</c>, row-level security included. No tenant, actor, key,
/// connection string or row value ever reaches a log: statements name entity, context and constraint types and closed
/// sets of causes, never composed text.
/// </summary>
internal static partial class EfCoreLog
{
    /// <summary>Logged by the concurrency-conflict translator just before it returns the translated exception.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.ConcurrencyConflictDetected,
        Level = LogLevel.Warning,
        Message = "Concurrency conflict detected for entity type '{EntityType}'.")]
    internal static partial void ConcurrencyConflictDetected(ILogger logger, string entityType);

    /// <summary>Logged once at the start of the migration and seed hosted service.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.MigrationAndSeedStarted,
        Level = LogLevel.Information,
        Message = "Migration and seed startup sequence started for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedStarted(ILogger logger, string contextType);

    /// <summary>Logged once per applied seeder.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.SeederApplied,
        Level = LogLevel.Debug,
        Message = "Seeder '{SeederType}' was applied for context '{ContextType}'.")]
    internal static partial void SeederApplied(ILogger logger, string seederType, string contextType);

    /// <summary>Logged when the migration and seed sequence completes.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.MigrationAndSeedCompleted,
        Level = LogLevel.Information,
        Message = "Migration and seed startup sequence completed for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedCompleted(ILogger logger, string contextType);

    /// <summary>
    /// Logged when a step of the migration and seed sequence (lock acquisition, migration or a seeder) throws, just
    /// before the exception is rethrown. The advisory lock is still released afterwards.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.MigrationAndSeedFailed,
        Level = LogLevel.Error,
        Message = "Migration and seed startup sequence failed for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedFailed(ILogger logger, string contextType, Exception exception);

    /// <summary>Logged after the PostgreSQL advisory lock is acquired. Never the lock key.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.AdvisoryLockAcquired,
        Level = LogLevel.Debug,
        Message = "Advisory lock was acquired for context '{ContextType}'.")]
    internal static partial void AdvisoryLockAcquired(ILogger logger, string contextType);

    /// <summary>Logged after the PostgreSQL advisory lock is released. Never the lock key.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.AdvisoryLockReleased,
        Level = LogLevel.Debug,
        Message = "Advisory lock was released for context '{ContextType}'.")]
    internal static partial void AdvisoryLockReleased(ILogger logger, string contextType);

    /// <summary>Logged when a retrying execution strategy exhausts all attempts, just before the final exception is rethrown unchanged.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.TransientRetryExhausted,
        Level = LogLevel.Warning,
        Message = "Transient fault retry was exhausted after {AttemptCount} attempts for a database operation.")]
    internal static partial void TransientRetryExhausted(ILogger logger, int attemptCount, Exception exception);

    /// <summary>
    /// Logged when a failed save is classified by its PostgreSQL SQLSTATE. Carries the violated constraint and table
    /// (internal schema names, never returned to the caller). Never the offending value.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.DatabaseErrorClassified,
        Level = LogLevel.Warning,
        Message = "Database error {SqlState} was classified as '{ErrorCode}' on constraint '{ConstraintName}' of table '{TableName}'.")]
    internal static partial void DatabaseErrorClassified(
        ILogger logger,
        string sqlState,
        string errorCode,
        string constraintName,
        string tableName);

    /// <summary>Logged at Error when startup migrations or seeders are configured but no migration lock is registered.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.NoMigrationLockRegistered,
        Level = LogLevel.Error,
        Message = "No IMigrationLock is registered for context '{ContextType}', so startup migration and seed coordination across replicas is not guaranteed.")]
    internal static partial void NoMigrationLockRegistered(ILogger logger, string contextType);

    /// <summary>Domain events were pending at save time but no <c>IDomainEventDispatcher</c> is attached.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.DomainEventsDiscarded,
        Level = LogLevel.Warning,
        Message = "Discarded {EventCount} domain events of types '{EventTypes}' because no IDomainEventDispatcher is registered.")]
    internal static partial void DomainEventsDiscarded(ILogger logger, int eventCount, string eventTypes);

    /// <summary>A write was rejected because the targeted row provably belongs to another tenant.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.TenantIsolationViolationProven,
        Level = LogLevel.Warning,
        Message = "Rejected a write to '{EntityType}' because the targeted row belongs to another tenant.")]
    internal static partial void TenantIsolationViolationProven(ILogger logger, string entityType);

    /// <summary>Startup: the aggregates of the context can raise domain events but no dispatcher is registered.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.NoDomainEventDispatcherRegistered,
        Level = LogLevel.Warning,
        Message = "No IDomainEventDispatcher is registered for context '{ContextType}', so the domain events of its aggregates are discarded at save time.")]
    internal static partial void NoDomainEventDispatcherRegistered(ILogger logger, string contextType);

    /// <summary>The current row could not be read after a concurrency conflict.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.ConflictRowLookupFailed,
        Level = LogLevel.Debug,
        Message = "The current row of '{EntityType}' could not be read after a concurrency conflict.")]
    internal static partial void ConflictRowLookupFailed(ILogger logger, string entityType, Exception exception);

    /// <summary>Startup: the model of the context was built and validated.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.ModelValidated,
        Level = LogLevel.Information,
        Message = "Model of context '{ContextType}' was validated at startup with {EntityTypeCount} entity types.")]
    internal static partial void ModelValidated(ILogger logger, string contextType, int entityTypeCount);

    /// <summary>Startup: the model of the context failed validation; the host start fails with a fixed message and this entry names the cause.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.ModelValidationFailed,
        Level = LogLevel.Error,
        Message = "Model of context '{ContextType}' failed validation at startup.")]
    internal static partial void ModelValidationFailed(ILogger logger, string contextType, Exception exception);

    /// <summary>A context resolved in the scope cannot share the unit of work transaction; it is refused at commit if it holds changes.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.ContextCannotJoinTransaction,
        Level = LogLevel.Debug,
        Message = "Context '{ContextType}' does not join the transaction of '{OwnerType}' with cause {Cause}, so its changes are refused at commit.")]
    internal static partial void ContextCannotJoinTransaction(ILogger logger, string contextType, string ownerType, ContextJoinBlock cause);

    /// <summary>The outermost operation succeeded but joined work failed, so the transaction was rolled back.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.RolledBackAfterJoinedFailure,
        Level = LogLevel.Warning,
        Message = "The transaction of '{ContextType}' was rolled back because work that joined it failed.")]
    internal static partial void RolledBackAfterJoinedFailure(ILogger logger, string contextType);

    /// <summary>
    /// An expected version (an <c>If-Match</c>) did not open for the aggregate it was used with, so it was treated as
    /// stale. Carries the aggregate type and the open result, never the token.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.EntityVersionRejected,
        Level = LogLevel.Warning,
        Message = "An expected version of '{EntityType}' was treated as stale with open result {OpenResult}.")]
    internal static partial void EntityVersionRejected(ILogger logger, string entityType, EntityVersionOpenResult openResult);

    /// <summary>The asynchronous key provider could not be asked for its current key; the key already loaded stays in use.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.EntityVersionKeyRefreshFailed,
        Level = LogLevel.Warning,
        Message = "The key that seals entity versions could not be refreshed, so the current key stays in use and is requested again later.")]
    internal static partial void EntityVersionKeyRefreshFailed(ILogger logger, Exception exception);

    /// <summary>The current version of a conflicting row could not be sealed, so the conflict carries no version.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.EntityVersionNotSealed,
        Level = LogLevel.Warning,
        Message = "The current version of '{EntityType}' could not be sealed, so the conflict is reported without it.")]
    internal static partial void EntityVersionNotSealed(ILogger logger, string entityType, Exception exception);

    /// <summary>
    /// The startup warm-up could not load the key that seals entity versions. Not fatal: the first request that issues or
    /// checks a version loads it instead.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.EntityVersionKeyWarmUpFailed,
        Level = LogLevel.Warning,
        Message = "The key that seals entity versions could not be loaded at startup, so the first request that issues or checks a version loads it instead.")]
    internal static partial void EntityVersionKeyWarmUpFailed(ILogger logger, Exception exception);

    /// <summary>
    /// The asynchronous key provider did not answer the startup warm-up in time. Host start goes on; the key keeps
    /// loading in the background.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.EntityVersionKeyWarmUpTimedOut,
        Level = LogLevel.Warning,
        Message = "The key that seals entity versions was not loaded within {Timeout} at startup, so it keeps loading in the background and an earlier request loads it itself.")]
    internal static partial void EntityVersionKeyWarmUpTimedOut(ILogger logger, TimeSpan timeout);

    /// <summary>
    /// Logged when a context moves onto the cross-tenant database role. Information although it can repeat: it is a
    /// security-relevant lifecycle event of the bypass.
    /// </summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.CrossTenantConnectionUsed,
        Level = LogLevel.Information,
        Message = "Context '{ContextType}' switched to the cross-tenant database role for an active cross-tenant scope.")]
    internal static partial void CrossTenantConnectionUsed(ILogger logger, string contextType);

    /// <summary>Logged by the startup coverage check for each tenant table that row-level security does not protect.</summary>
    [LoggerMessage(
        EventId = EfCoreLoggingEventIds.TenantTableNotProtected,
        Level = LogLevel.Warning,
        Message = "Row-level security is enabled for context '{ContextType}', but tenant table '{TableName}' is not protected, with gap {Gap}.")]
    internal static partial void TenantTableNotProtected(ILogger logger, string contextType, string tableName, RowLevelSecurityGap gap);
}

/// <summary>Why a context cannot share the unit of work connection and transaction: a closed set, so a log carries no composed text.</summary>
internal enum ContextJoinBlock
{
    /// <summary>The context runs a transaction of its own.</summary>
    OwnTransaction,

    /// <summary>The context connects to another database, as another role, or on the other side of the cross-tenant switch.</summary>
    OtherDatabaseOrRole,

    /// <summary>The own connection of the context is already open.</summary>
    OpenConnection,
}

/// <summary>How a tenant table falls short of row-level security: a closed set, in the order the coverage check tests them.</summary>
internal enum RowLevelSecurityGap
{
    /// <summary>The table does not exist.</summary>
    TableMissing,

    /// <summary>Row-level security is not enabled on the table.</summary>
    RowSecurityDisabled,

    /// <summary>Row-level security is enabled but not forced, so the table owner bypasses it.</summary>
    RowSecurityNotForced,

    /// <summary>No policy of the table reads the tenant setting.</summary>
    TenantPolicyMissing,
}
