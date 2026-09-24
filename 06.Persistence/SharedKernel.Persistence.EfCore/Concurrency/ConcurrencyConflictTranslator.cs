using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>
/// Turns every <see cref="DbUpdateConcurrencyException"/> into a typed exception: a <see cref="ConflictException"/>,
/// whether the row changed, was deleted or belongs to another tenant.
/// </summary>
/// <remarks>
/// <para>
/// The former translator reported a tenant-isolation violation for any concurrency failure on a tenanted entity
/// without an <c>IHasConcurrency</c> token — a plain concurrent update or delete then surfaced as a 403 and
/// polluted the security metric, while a non-tenanted failure escaped as a 500. Now the current row is read
/// (ignoring query filters) and:
/// </para>
/// <list type="bullet">
/// <item><description>the row exists and its <c>TenantId</c> differs from the one the write assumed — a detached
/// entity carrying another tenant's key — proven violation: logged and counted as a tenant-isolation violation, but
/// answered with the same <see cref="ConflictException"/> (no version) as a deleted row, so the response never reveals
/// that the id exists in another tenant;</description></item>
/// <item><description>otherwise (the row changed, or was deleted, or is invisible under row-level security):
/// <see cref="ConflictException"/> with the row's current version attached when it still exists — sealed like every
/// version (<see cref="ConcurrencyVersion"/>), never the raw <c>xmin</c>.</description></item>
/// </list>
/// <para>
/// If the row cannot be read (for example the connection broke), or the service registers no key provider to seal
/// versions with, the result is a conflict without a version.
/// </para>
/// </remarks>
internal static class ConcurrencyConflictTranslator
{
    /// <summary>Translates <paramref name="exception"/>, reading the current row asynchronously.</summary>
    public static async Task<Exception> TranslateAsync(
        DbContext context,
        DbUpdateConcurrencyException exception,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var entry = exception.Entries.FirstOrDefault();
        if (entry is null)
            return ToConflict(context, null, null, exception, logger);

        PropertyValues? current;
        try
        {
            current = await entry.GetDatabaseValuesAsync(cancellationToken);
        }
        catch (Exception lookupFailure) when (lookupFailure is not OperationCanceledException)
        {
            PersistenceContextLog.ConflictRowLookupFailed(logger, lookupFailure, entry.Metadata.ClrType.Name);
            return ToConflict(context, entry, null, exception, logger);
        }

        return Classify(context, entry, current, exception, logger);
    }

    /// <summary>Translates <paramref name="exception"/>, reading the current row synchronously.</summary>
    public static Exception Translate(DbContext context, DbUpdateConcurrencyException exception, ILogger logger)
    {
        var entry = exception.Entries.FirstOrDefault();
        if (entry is null)
            return ToConflict(context, null, null, exception, logger);

        PropertyValues? current;
        try
        {
            current = entry.GetDatabaseValues();
        }
        catch (Exception lookupFailure)
        {
            PersistenceContextLog.ConflictRowLookupFailed(logger, lookupFailure, entry.Metadata.ClrType.Name);
            return ToConflict(context, entry, null, exception, logger);
        }

        return Classify(context, entry, current, exception, logger);
    }

    private static Exception Classify(
        DbContext context,
        EntityEntry entry,
        PropertyValues? current,
        DbUpdateConcurrencyException exception,
        ILogger logger)
    {
        if (current is not null && entry.Entity is IHasTenant)
        {
            var tenantId = nameof(IHasTenant.TenantId);
            var assumed = entry.OriginalValues[tenantId];
            var actual = current[tenantId];

            if (!Equals(assumed, actual))
            {
                var entityTypeName = entry.Metadata.ClrType.Name;
                PersistenceContextLog.TenantIsolationViolationProven(logger, entityTypeName);
                PersistenceMeter.TenantIsolationViolations.Add(1,
                    new KeyValuePair<string, object?>(PersistenceTagKeys.AggregateType, entityTypeName));

                // Answered exactly like a row that does not exist: a different answer (403 vs 409) would let a caller probe
                // which ids exist in other tenants. The violation is visible only in the log and the metric.
                return ToConflict(context, entry, current: null, exception, logger);
            }
        }

        return ToConflict(context, entry, current, exception, logger);
    }

    private static ConflictException ToConflict(
        DbContext context,
        EntityEntry? entry,
        PropertyValues? current,
        DbUpdateConcurrencyException exception,
        ILogger logger)
    {
        var entityTypeName = entry?.Metadata.ClrType.Name ?? "entity";

        // The row's current version, sealed like every version that leaves the persistence layer — never the raw xmin.
        // No version when the row is gone, belongs to another tenant, or the service registers no key provider.
        EntityVersion? currentVersion = null;
        if (entry is not null && current is not null && ConcurrencyVersion.FindToken(entry.Metadata) is { } token)
            currentVersion = ConcurrencyVersion.TrySeal(context, entry, ConcurrencyVersion.ToVersion(current[token.Name]));

        PersistenceLog.ConcurrencyConflictDetected(logger, entityTypeName);
        PersistenceMeter.ConcurrencyConflicts.Add(1,
            new KeyValuePair<string, object?>(PersistenceTagKeys.AggregateType, entityTypeName));

        return ConcurrencyVersion.Conflict(entityTypeName, currentVersion, exception);
    }
}
