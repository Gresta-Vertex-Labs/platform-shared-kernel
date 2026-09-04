using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Persistence.Abstractions.Auditing;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core save-changes interceptor that structurally forbids updating or deleting an existing
/// <see cref="AuditRecord"/> row.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457/D-123. A fourth interceptor, alongside the platform's three
/// (Audit/SoftDelete/Concurrency) — registered ONLY when
/// <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> has been called. This is the
/// LOAD-BEARING piece of the audit trail's append-only guarantee: a structural guard enforced at
/// the ORM save boundary, not merely a convention that a well-behaved caller happens to follow.
/// </para>
/// <para>
/// Unlike <c>ConcurrencyInterceptor</c> (which discovered that exceptions thrown from
/// <c>SaveChangesFailed</c>/<c>SaveChangesFailedAsync</c> are swallowed by EF Core 10's own
/// interceptor dispatcher), this interceptor throws from <see cref="SavingChanges"/>/
/// <see cref="SavingChangesAsync"/> — hooks that run BEFORE any command is sent to the database and
/// whose thrown exceptions DO propagate normally out of <c>DbContext.SaveChanges</c>/<c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// <strong>DEFENSE IN DEPTH — RECOMMENDED:</strong> also apply a database-level
/// <c>REVOKE UPDATE, DELETE</c> grant on the underlying table, mirroring this package's existing
/// encryption/soft-delete documentation style. An ORM-level guard alone cannot stop a write issued
/// outside this application (a raw SQL console, a different service sharing the database).
/// </para>
/// <para>
/// <see cref="EfAuditTrailWriter"/> calls <c>SaveChangesAsync</c> on the SAME <see cref="DbContext"/>
/// this interceptor is registered on, so this interceptor fires on every audit write too —
/// harmless, since a freshly-<see cref="EntityState.Added"/> <see cref="AuditRecord"/> is never
/// <see cref="EntityState.Modified"/>/<see cref="EntityState.Deleted"/>.
/// </para>
/// </remarks>
public sealed class AuditRecordImmutabilityInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Throws AuditRecordImmutableException for the first AuditRecord entry found in Modified or
    // Deleted state. Added and Unchanged entries are never affected.
    private static void Guard(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<AuditRecord>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new AuditRecordImmutableException(entry.Entity.Id);
        }
    }
}
