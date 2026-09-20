using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that converts physical deletes into soft deletes for
/// entities implementing <see cref="ISoftDeletable"/>, and preserves everything EF Core's own
/// cascade-delete fixup marked <see cref="EntityState.Deleted"/> as a side effect.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="SavingChangesAsync"/> / <see cref="SavingChanges"/>, for every entry in
/// <see cref="EntityState.Deleted"/> state whose entity implements <see cref="ISoftDeletable"/>
/// and was explicitly removed (a root aggregate, or any other <see cref="ISoftDeletable"/> entity
/// removed on its own terms) this interceptor:
/// <list type="number">
/// <item><description>Changes the entry state from <c>Deleted</c> to <c>Modified</c>.</description></item>
/// <item><description>Sets <c>IsDeleted = true</c>.</description></item>
/// <item><description>Sets <c>DeletedOn</c> to the current UTC timestamp.</description></item>
/// <item><description>Sets <c>DeletedBy</c> to the current actor identifier.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Cascade fix:</strong> removing a soft-deletable aggregate root via
/// <c>Set&lt;T&gt;().Remove(aggregate)</c> makes EF Core's own cascade-delete fixup walk every owned
/// value object (<c>OwnsOne</c>/<c>OwnsMany</c>) and cascade-configured dependent, marking each of
/// them <c>Deleted</c> too — even though the root itself is about to become <c>Modified</c>
/// (soft-deleted), not physically removed. Left unhandled, those dependents were still hard-deleted
/// (or, for an optional owned collection, orphaned with a nulled foreign key) while the root row
/// survived — a genuinely corrupt, inconsistent snapshot: a "deleted" order whose line items are
/// simply gone. For every root this interceptor soft-deletes, it walks the reachable graph of
/// entries EF's fixup marked <c>Deleted</c> alongside it (via <see cref="IReadOnlyEntityType.FindOwnership"/>
/// for owned types, and current foreign-key-value matching for ordinary cascade-configured
/// dependents) and either cascades the soft-delete to them (when they are themselves
/// <see cref="ISoftDeletable"/>) or restores them to <see cref="EntityState.Unchanged"/> (when they
/// have no delete state of their own and must simply keep existing). A child <em>deliberately</em>
/// removed from an owned collection while its owner stays <see cref="EntityState.Unchanged"/> or
/// <see cref="EntityState.Modified"/> (not <c>Deleted</c>) is never touched by this walk — that
/// physical delete is legitimate application behavior, not a cascade artifact.
/// </para>
/// <para>
/// <strong>Actor resolution:</strong> <c>DeletedBy</c> is populated from
/// <see cref="ICurrentActorContext.ActorId"/> — see <see cref="AuditInterceptor"/>'s remarks for the
/// full rationale (identical here). This interceptor no longer references
/// <c>SharedKernel.Security.Abstractions</c> at all.
/// </para>
/// <para>
/// <strong>Mutation rule:</strong> All field writes go exclusively through
/// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c>. Direct property setters on
/// entity instances are never called.
/// </para>
/// <para>
/// Soft-deleted records are hidden from normal queries by the named global query filter
/// <c>"SoftDelete"</c> configured by <c>EntityTypeConfigurationBase</c>.
/// Use <c>spec.IncludeDeleted = true</c> on a specification to bypass this filter via the
/// <c>SpecificationEvaluator</c>; do not call <c>.IgnoreQueryFilters()</c> directly.
/// </para>
/// </remarks>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentActorContext _actorContext;
    private readonly IClock _clock;

    /// <summary>
    /// Initialises a new <see cref="SoftDeleteInterceptor"/> with the required dependencies.
    /// </summary>
    /// <param name="actorContext">
    /// Scoped DI dependency providing the current actor's identity.
    /// </param>
    /// <param name="clock">
    /// Abstracted system clock for deterministic timestamp production.
    /// </param>
    public SoftDeleteInterceptor(ICurrentActorContext actorContext, IClock clock)
    {
        _actorContext = actorContext;
        _clock = clock;
    }

    /// <summary>
    /// Gets the <see cref="ICurrentActorContext"/> captured at construction time.
    /// </summary>
    /// <remarks>
    /// Retained for symmetry with <see cref="AuditInterceptor.ActorContext"/>, though
    /// unlike that type, <see cref="SharedKernelDbContext.CurrentActor"/> is initialised from
    /// <see cref="AuditInterceptor"/> alone (both interceptors are always constructed with the same
    /// scoped <see cref="ICurrentActorContext"/>, so either would produce an identical initial value).
    /// This interceptor no longer reads this field directly inside <see cref="ApplySoftDelete"/>.
    /// </remarks>
    internal ICurrentActorContext ActorContext => _actorContext;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Converts Deleted state to Modified for ISoftDeletable entities, and rescues every dependent
    // EF's own cascade-delete fixup marked Deleted alongside a soft-deleted root — see class remarks.
    //
    // Resolves the current ICurrentActorContext LIVE off
    // ((SharedKernelDbContext)context).CurrentActor — see AuditInterceptor.ApplyAudit's remarks for
    // the full pooling-safety rationale; identical reasoning applies here.
    private void ApplySoftDelete(DbContext? context)
    {
        if (context is null) return;

        var actorContext = ((SharedKernelDbContext)context).CurrentActor;
        var userId = actorContext.ActorId;
        var now = _clock.UtcNow;
        var changeTracker = context.ChangeTracker;

        // Snapshot up front: converting a root's state below never changes the deleted-ness of
        // other entries, but iterating and mutating the same live query would be fragile.
        var deletedSoftDeletableRoots = changeTracker.Entries<ISoftDeletable>()
            .Where(e => e.State == EntityState.Deleted)
                .ToList();

        var visited = new HashSet<object>();

        foreach (var rootEntry in deletedSoftDeletableRoots)
        {
            SoftDelete(rootEntry, now, userId);
            RescueCascadedDependents(changeTracker, rootEntry, now, userId, visited);
        }
    }

    private static void SoftDelete(EntityEntry entry, DateTimeOffset now, string userId)
    {
        entry.State = EntityState.Modified;
        entry.CurrentValues[nameof(ISoftDeletable.IsDeleted)] = true;
        entry.CurrentValues[nameof(ISoftDeletable.DeletedOn)] = now;
        entry.CurrentValues[nameof(ISoftDeletable.DeletedBy)] = userId;
    }

    // Walks every OTHER entry EF's cascade-delete fixup marked Deleted because it is an owned type
    // or cascade-configured dependent of `ownerEntry`, and either cascades the soft-delete to it (when
    // it is itself ISoftDeletable) or restores it to Unchanged (when it must simply keep existing).
    // Recurses so a multi-level owned graph (owned collection of owned references, etc.) is fully
    // rescued. `visited` prevents revisiting the same entity twice through more than one navigation
    // path and bounds recursion to the graph actually reachable from a deleted root.
    private static void RescueCascadedDependents(
        ChangeTracker changeTracker,
        EntityEntry ownerEntry,
        DateTimeOffset now,
        string userId,
        HashSet<object> visited)
    {
        if (!visited.Add(ownerEntry.Entity))
            return;

        var stillDeleted = changeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted && !visited.Contains(e.Entity))
                .ToList();

        foreach (var dependentEntry in stillDeleted)
        {
            if (!IsDependentOf(dependentEntry, ownerEntry))
                continue;

            if (dependentEntry.Entity is ISoftDeletable)
                SoftDelete(dependentEntry, now, userId);
            else
                dependentEntry.State = EntityState.Unchanged;

            RescueCascadedDependents(changeTracker, dependentEntry, now, userId, visited);
        }
    }

    // True when `dependentEntry` carries a foreign key referencing `ownerEntry`'s own entity type
    // whose CURRENT value matches ownerEntry's current key — correct for both EF "owned type"
    // relationships (OwnsOne/OwnsMany, always required+cascade by EF Core default) and an ordinary
    // one-to-many navigation with DeleteBehavior.Cascade.
    private static bool IsDependentOf(EntityEntry dependentEntry, EntityEntry ownerEntry)
    {
        foreach (var foreignKey in dependentEntry.Metadata.GetForeignKeys())
        {
            if (foreignKey.PrincipalEntityType.ClrType != ownerEntry.Metadata.ClrType)
                continue;

            var principalKeyProperties = foreignKey.PrincipalKey.Properties;
            var dependentProperties = foreignKey.Properties;
            var matches = true;

            for (var i = 0; i < dependentProperties.Count; i++)
            {
                var fkValue = dependentEntry.Property(dependentProperties[i].Name).CurrentValue;
                var pkValue = ownerEntry.Property(principalKeyProperties[i].Name).CurrentValue;

                if (!Equals(fkValue, pkValue))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }
}
