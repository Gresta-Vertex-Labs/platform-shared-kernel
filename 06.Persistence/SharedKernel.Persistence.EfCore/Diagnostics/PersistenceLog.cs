using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> source-generated log statements for <c>SharedKernel.Persistence.EfCore</c>.
/// </summary>
/// <remarks>
/// <para>
/// Occupies EventId sub-block <see cref="LoggingEventIdRanges.Persistence"/>..<c>+99</c>
/// (<c>6000-6099</c>) — <c>SharedKernel.Persistence.EfCore</c>'s claim on <c>01.Core</c>'s
/// <c>06.Persistence</c> <c>6000-6999</c> block. <c>SharedKernel.Persistence.Abstractions</c> — a
/// pure interface library with no DI-resolved <see cref="ILogger"/> consumer — reserves no
/// sub-block at all; a future package that starts logging (<c>.PostgreSQL</c>, <c>.Dapper</c>)
/// claims the next unclaimed 100-wide slot (<c>6100-6199</c>, then <c>6200-6299</c>) at that time,
/// never renumbering this block retroactively.
/// </para>
/// <para>
/// <strong>Never a raw <c>ILogger.LogX(...)</c> call anywhere in this domain</strong> — every
/// production log statement is one of the source-generated partial methods below.
/// <see cref="EncryptionRotationBatchProcessed"/>/<see cref="EncryptionRotationCompleted"/> never
/// log a key byte, a Base64-encoded key string, or any column plaintext/ciphertext value — only
/// counts and already-non-secret version-tag strings (e.g. <c>"v1"</c>).
/// </para>
/// </remarks>
internal static partial class PersistenceLog
{
    /// <summary>
    /// Logged by <c>ConcurrencyInterceptor.TryTranslate</c> immediately before it returns the
    /// translated <see cref="SharedKernel.Core.Exceptions.ConflictException"/> — never logs the row
    /// payload, only the conflicting entry's CLR type name.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 0,
        Level = LogLevel.Warning,
        Message = "Concurrency conflict detected for entity type '{EntityType}'.")]
    internal static partial void ConcurrencyConflictDetected(ILogger logger, string entityType);

    /// <summary>Logged once at the start of <c>MigrationAndSeedHostedService.StartAsync</c>.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 1,
        Level = LogLevel.Information,
        Message = "Migration and seed startup sequence started for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedStarted(ILogger logger, string contextType);

    /// <summary>Logged once per successfully-applied <see cref="Seeding.IDataSeeder{TContext}"/>.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 2,
        Level = LogLevel.Information,
        Message = "Seeder '{SeederType}' applied successfully for context '{ContextType}'.")]
    internal static partial void SeederApplied(ILogger logger, string seederType, string contextType);

    /// <summary>Logged when <c>MigrationAndSeedHostedService.StartAsync</c> completes successfully.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 3,
        Level = LogLevel.Information,
        Message = "Migration and seed startup sequence completed for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedCompleted(ILogger logger, string contextType);

    /// <summary>
    /// Logged when any step of <c>MigrationAndSeedHostedService.StartAsync</c> (advisory-lock
    /// acquisition, migration, or a seeder) throws, immediately before the exception is rethrown.
    /// The advisory lock is still released after this log fires — logging never disturbs the
    /// existing <c>finally</c>-block release ordering.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 4,
        Level = LogLevel.Warning,
        Message = "Migration and seed startup sequence failed for context '{ContextType}'.")]
    internal static partial void MigrationAndSeedFailed(ILogger logger, Exception exception, string contextType);

    /// <summary>
    /// Logged immediately after the PostgreSQL advisory lock is acquired. Never logs the lock key
    /// value, the connection string, or any other secret/sensitive value — only the CLR type name
    /// of the <c>TContext</c> the lock guards.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 5,
        Level = LogLevel.Information,
        Message = "Advisory lock acquired for context '{ContextType}'.")]
    internal static partial void AdvisoryLockAcquired(ILogger logger, string contextType);

    /// <summary>
    /// Logged immediately after the PostgreSQL advisory lock is released. Never logs the lock key
    /// value, the connection string, or any other secret/sensitive value — only the CLR type name
    /// of the <c>TContext</c> the lock guarded.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 6,
        Level = LogLevel.Information,
        Message = "Advisory lock released for context '{ContextType}'.")]
    internal static partial void AdvisoryLockReleased(ILogger logger, string contextType);

    /// <summary>
    /// Logged once per retry attempt by the internal
    /// <c>PersistenceRetryDiagnosticListener</c>, registered only when
    /// <c>EfCorePersistenceBuilder.WithTransientFaultRetry()</c> was called.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 7,
        Level = LogLevel.Warning,
        Message = "Transient fault triggered retry attempt {AttemptNumber} for a database operation.")]
    internal static partial void TransientRetryAttempt(ILogger logger, int attemptNumber);

    /// <summary>
    /// Logged by <c>EfUnitOfWork.SaveChangesAsync</c> / <c>EfTransactionalUnitOfWork</c> when a
    /// configured retrying execution strategy exhausts all attempts, immediately before the final
    /// exception is rethrown unchanged.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 8,
        Level = LogLevel.Warning,
        Message = "Transient fault retry exhausted after {AttemptCount} attempt(s) for a database operation.")]
    internal static partial void TransientRetryExhausted(ILogger logger, Exception exception, int attemptCount);

    // The former EncryptionRotationBatchProcessed/EncryptionRotationCompleted/
    // EncryptionKeyRefreshFailed/EncryptionKeyOnDemandWarmFailed moved to
    // SharedKernel.Persistence.EfCore.Encryption's own EncryptionLog, which claims its own dedicated
    // 6300-6399 sub-block (SharedKernel.Persistence.EfCore.Encryption/Encryption/Diagnostics/
    // EncryptionLog.cs) — this class is `internal` and therefore unreachable from that sibling
    // package. IDs 6009-6012 were never actually used by it and remain free within this sub-block,
    // alongside every ID from 6014 up. The entry below claims 6013, the first gap left by 6000-6008.

    /// <summary>
    /// Logged, at Error level, by <c>MigrationAndSeedHostedService.StartAsync</c> when migrations-on-startup
    /// or at least one seeder is configured but no <see cref="SharedKernel.Persistence.Abstractions.Coordination.IMigrationLock"/>
    /// is registered — startup coordination across replicas is NOT guaranteed in that configuration.
    /// Never a hard failure: a deliberately single-replica or non-PostgreSQL
    /// deployment proceeds without a lock.
    /// </summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 13,
        Level = LogLevel.Error,
        Message = "No IMigrationLock is registered for context '{ContextType}' — startup migration/seed coordination across replicas is NOT guaranteed.")]
    internal static partial void NoMigrationLockRegistered(ILogger logger, string contextType);
}
