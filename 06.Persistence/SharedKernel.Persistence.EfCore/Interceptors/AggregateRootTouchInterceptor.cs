using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that marks an aggregate root <see cref="EntityState.Modified"/>
/// when only one of its owned children changed, so the root's own row — and therefore its
/// concurrency token — advances too.
/// </summary>
/// <remarks>
/// <para>
/// An owned collection (<c>OwnsMany</c>) is mapped to its own table. Adding,
/// changing, or removing an element of that collection produces an <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>
/// against the CHILD table only — the aggregate root's own row, and hence its
/// <see cref="IHasConcurrency.RowVersion"/> (bound to PostgreSQL's <c>xmin</c> by
/// <c>SharedKernel.Persistence.EfCore</c>), is never touched. Two concurrent requests that each
/// modify a different line item of the same order would both succeed even though, from the
/// aggregate's own consistency boundary, they raced on the SAME aggregate — optimistic concurrency
/// silently does not apply. This interceptor closes that gap for the common case: an owned-type
/// child (an <c>OwnsOne</c>/<c>OwnsMany</c> value object) whose own row shares no table with an
/// <see cref="EntityState.Unchanged"/> root is walked up to that root via
/// <see cref="Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType.FindOwnership"/>, and the
/// root is forced <see cref="EntityState.Modified"/> — EF Core then reissues a full-column
/// <c>UPDATE</c> for the root's own row (concurrency-token-checked exactly like any other update),
/// which is what advances the token.
/// </para>
/// <para>
/// <strong>Deliberately NOT touched:</strong> <see cref="Domain.IHasVersion.Version"/> — that
/// property's documented meaning is the count of raised domain events, not a generic dirty counter,
/// and this interceptor must never give it a second, conflicting meaning. Concurrency protection
/// here is carried entirely by <see cref="IHasConcurrency.RowVersion"/>, exactly as for any other
/// change to the root.
/// </para>
/// <para>
/// A child that shares the root's own table (a same-table <c>OwnsOne</c> reference, or a scalar
/// property) already advances the root's row/token automatically — EF Core issues one <c>UPDATE</c>
/// for the whole row in that case, so this interceptor has nothing to do for it and correctly leaves
/// it alone (its owner is never <see cref="EntityState.Unchanged"/> to begin with).
/// </para>
/// </remarks>
public sealed class AggregateRootTouchInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        TouchOwningRoots(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        TouchOwningRoots(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void TouchOwningRoots(DbContext? context)
    {
        if (context is null) return;

        var changeTracker = context.ChangeTracker;

        // Snapshot: only owned-type entries with an actual pending write, whose immediate owner is
        // NOT already going to be written this SaveChanges pass (Modified/Added roots already touch
        // their own row — nothing to force).
        var changedOwnedEntries = changeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Metadata.FindOwnership() is not null)
            .ToList();

        foreach (var ownedEntry in changedOwnedEntries)
            TouchRootIfUnchanged(changeTracker, ownedEntry, []);
    }

    // Walks up the ownership chain from `entry` to its top-level (non-owned) root, forcing the FIRST
    // Unchanged ancestor found to Modified — that single UPDATE is enough to advance the root row's
    // concurrency token; no need to continue past it. `visited` guards against a pathological
    // ownership cycle (never expected in practice, but the walk must still terminate).
    private static void TouchRootIfUnchanged(ChangeTracker changeTracker, EntityEntry entry, HashSet<object> visited)
    {
        if (!visited.Add(entry.Entity))
            return;

        var ownership = entry.Metadata.FindOwnership();
        if (ownership is null)
            return; // Reached a non-owned entry — nothing further to walk.

        var principalKeyProperties = ownership.PrincipalKey.Properties;
        var dependentProperties = ownership.Properties;

        foreach (var candidate in changeTracker.Entries())
        {
            if (candidate.Metadata.ClrType != ownership.PrincipalEntityType.ClrType)
                continue;

            var matches = true;
            for (var i = 0; i < dependentProperties.Count; i++)
            {
                var fkValue = entry.Property(dependentProperties[i].Name).CurrentValue;
                var pkValue = candidate.Property(principalKeyProperties[i].Name).CurrentValue;

                if (!Equals(fkValue, pkValue))
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
                continue;

            if (candidate.State == EntityState.Unchanged)
                candidate.State = EntityState.Modified;

            if (candidate.State is EntityState.Modified or EntityState.Added)
                return; // The owner already touches its own row this pass — done.

            TouchRootIfUnchanged(changeTracker, candidate, visited);
            return;
        }
    }
}
