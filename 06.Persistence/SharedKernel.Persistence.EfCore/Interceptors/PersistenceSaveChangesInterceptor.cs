using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// The platform's one save-changes interceptor: soft delete, aggregate-root touch, audit stamps and the tenant
/// write guard, in that order, over one change-detection pass and one snapshot of the tracked entries.
/// </summary>
/// <remarks>
/// <para>
/// Replaces four interceptors that each enumerated <c>ChangeTracker.Entries()</c> (which runs change detection)
/// once per entry they visited — quadratic in the number of tracked entities. This one detects changes once,
/// works on a snapshot with automatic detection switched off, and resolves principals through a key index.
/// </para>
/// <list type="number">
/// <item><description><strong>Soft delete.</strong> A deleted <see cref="ISoftDeletable"/> entity becomes
/// modified with <c>IsDeleted</c>/<c>DeletedOn</c>/<c>DeletedBy</c> set. Entities EF's cascade marked deleted
/// with it are soft-deleted too when soft-deletable, otherwise restored to unchanged — a soft-deleted order keeps
/// its lines. A child removed on its own while its owner stays is left deleted.</description></item>
/// <item><description><strong>Aggregate-root touch.</strong> When a child changes but its aggregate root does
/// not, the root is marked modified, so its row is updated and its <c>xmin</c> concurrency token checked and
/// advanced. A child is an owned entity (walked up its ownership) or a non-owned entity with a required foreign
/// key to an aggregate root.</description></item>
/// <item><description><strong>Audit.</strong> Added <see cref="IHasCreatedAudit"/> entities get
/// <c>CreatedBy</c>/<c>CreatedOn</c>; modified <see cref="IHasAudit"/> entities get <c>ModifiedBy</c>/<c>ModifiedOn</c>.
/// The actor is the caller's user id, else the configured service name. <c>CreatedBy</c>/<c>CreatedOn</c> are
/// never written by an update (a model convention sets their after-save behavior to ignore).</description></item>
/// <item><description><strong>Tenant write guard</strong> (<see cref="TenantedDbContext"/> only, skipped while a
/// cross-tenant scope is active). An added, modified or deleted <see cref="IHasTenant"/> entity must belong to the
/// current tenant and must not change its tenant; with no current tenant every such write is rejected.</description></item>
/// </list>
/// <para>
/// Stateless and shared by every context: the caller, the clock and the service name are read from the context
/// being saved.
/// </para>
/// </remarks>
internal sealed class PersistenceSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<Type, bool> AggregateRootTypes = new();

    private PersistenceSaveChangesInterceptor()
    {
    }

    /// <summary>The shared instance every context registers.</summary>
    public static PersistenceSaveChangesInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>Returns whether <paramref name="clrType"/> implements <see cref="IAggregateRoot{TId}"/>.</summary>
    internal static bool IsAggregateRootClrType(Type clrType) =>
        AggregateRootTypes.GetOrAdd(clrType, static type => type.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAggregateRoot<>)));

    /// <summary>Returns whether <paramref name="entityType"/> is a (non-owned) aggregate root.</summary>
    internal static bool IsAggregateRoot(IReadOnlyEntityType entityType) =>
        !entityType.IsOwned() && IsAggregateRootClrType(entityType.ClrType);

    private static void Apply(DbContext? context)
    {
        if (context is not SharedKernelDbContext sharedKernelContext)
            return;

        var tracker = context.ChangeTracker;
        var autoDetect = tracker.AutoDetectChangesEnabled;
        if (autoDetect)
            tracker.DetectChanges();

        tracker.AutoDetectChangesEnabled = false;
        try
        {
            var entries = tracker.Entries().ToList();
            if (entries.Count == 0)
                return;

            var index = new EntryIndex(entries);
            var actor = sharedKernelContext.CurrentActorId;
            var now = sharedKernelContext.Clock.UtcNow;

            ApplySoftDelete(entries, index, actor, now);
            TouchAggregateRoots(entries, index);
            StampAudit(entries, actor, now);

            if (context is TenantedDbContext tenanted && !CrossTenantScope.IsActiveInCurrentFlow)
                GuardTenant(entries, tenanted.CurrentTenantId);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private static void ApplySoftDelete(List<EntityEntry> entries, EntryIndex index, string actor, DateTimeOffset now)
    {
        List<EntityEntry>? roots = null;
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable)
                (roots ??= []).Add(entry);
        }

        if (roots is null)
            return;

        // Every deleted entry, grouped under the tracked principal its foreign keys point at.
        var deletedDependents = new Dictionary<EntityEntry, List<EntityEntry>>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (entry.State != EntityState.Deleted)
                continue;

            foreach (var foreignKey in entry.Metadata.GetForeignKeys())
            {
                if (index.FindPrincipal(entry, foreignKey) is not { } principal)
                    continue;

                if (!deletedDependents.TryGetValue(principal, out var list))
                    deletedDependents[principal] = list = [];
                list.Add(entry);
            }
        }

        var visited = new HashSet<EntityEntry>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<EntityEntry>();
        foreach (var root in roots)
        {
            if (!visited.Add(root))
                continue;

            SoftDelete(root, actor, now);
            queue.Enqueue(root);

            while (queue.TryDequeue(out var owner))
            {
                if (!deletedDependents.TryGetValue(owner, out var dependents))
                    continue;

                foreach (var dependent in dependents)
                {
                    if (dependent.State != EntityState.Deleted || !visited.Add(dependent))
                        continue;

                    if (dependent.Entity is ISoftDeletable)
                        SoftDelete(dependent, actor, now);
                    else
                        dependent.State = EntityState.Unchanged;

                    queue.Enqueue(dependent);
                }
            }
        }
    }

    private static void SoftDelete(EntityEntry entry, string actor, DateTimeOffset now)
    {
        entry.State = EntityState.Modified;
        entry.CurrentValues[nameof(ISoftDeletable.IsDeleted)] = true;
        entry.CurrentValues[nameof(ISoftDeletable.DeletedOn)] = now;
        entry.CurrentValues[nameof(ISoftDeletable.DeletedBy)] = actor;
    }

    private static void TouchAggregateRoots(List<EntityEntry> entries, EntryIndex index)
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (IsAggregateRoot(entry.Metadata))
                continue;

            if (FindRoot(entry, index) is { State: EntityState.Unchanged } root)
                root.State = EntityState.Modified;
        }
    }

    // Walks from a changed child to its top-most tracked parent: up the ownership chain, then across a required
    // foreign key to an aggregate root. Returns null when the entry has no tracked parent.
    private static EntityEntry? FindRoot(EntityEntry entry, EntryIndex index)
    {
        var current = entry;
        var visited = new HashSet<EntityEntry>(ReferenceEqualityComparer.Instance) { entry };

        while (true)
        {
            var parent = FindParent(current, index);
            if (parent is null || !visited.Add(parent))
                return ReferenceEquals(current, entry) ? null : current;

            if (IsAggregateRoot(parent.Metadata))
                return parent;

            current = parent;
        }
    }

    private static EntityEntry? FindParent(EntityEntry entry, EntryIndex index)
    {
        if (entry.Metadata.FindOwnership() is { } ownership)
            return index.FindPrincipal(entry, ownership);

        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            if (foreignKey.IsRequired && IsAggregateRoot(foreignKey.PrincipalEntityType)
                && index.FindPrincipal(entry, foreignKey) is { } principal)
            {
                return principal;
            }
        }

        return null;
    }

    private static void StampAudit(List<EntityEntry> entries, string actor, DateTimeOffset now)
    {
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added && entry.Entity is IHasCreatedAudit)
            {
                entry.CurrentValues[nameof(IHasCreatedAudit.CreatedBy)] = actor;
                entry.CurrentValues[nameof(IHasCreatedAudit.CreatedOn)] = now;
            }
            else if (entry.State == EntityState.Modified && entry.Entity is IHasAudit)
            {
                entry.CurrentValues[nameof(IHasAudit.ModifiedBy)] = actor;
                entry.CurrentValues[nameof(IHasAudit.ModifiedOn)] = now;
            }
        }
    }

    private static void GuardTenant(List<EntityEntry> entries, Guid? currentTenantId)
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || entry.Entity is not IHasTenant tenanted)
            {
                continue;
            }

            var entityTypeName = entry.Entity.GetType().Name;

            // Compare values, never IsModified: a state set directly (soft delete, touch) marks every
            // property modified whether or not its value changed.
            var tenantProperty = entry.Property(nameof(IHasTenant.TenantId));
            if (entry.State == EntityState.Modified && !Equals(tenantProperty.CurrentValue, tenantProperty.OriginalValue))
                throw Reject(entityTypeName, "changed the TenantId of");

            if (currentTenantId is null || tenanted.TenantId != currentTenantId.Value)
                throw Reject(entityTypeName, "written outside the current tenant for");
        }
    }

    private static ForbiddenException Reject(string entityTypeName, string reason)
    {
        PersistenceMeter.TenantIsolationViolations.Add(1,
            new KeyValuePair<string, object?>(PersistenceTagKeys.AggregateType, entityTypeName));
        return new ForbiddenException(TenantIsolationErrors.Build(entityTypeName, reason));
    }

    // Resolves the tracked principal of a foreign key through the key values of the snapshot, built lazily per key.
    private sealed class EntryIndex(List<EntityEntry> entries)
    {
        private readonly Dictionary<IReadOnlyKey, Dictionary<KeyValues, EntityEntry>> _byKey = [];

        public EntityEntry? FindPrincipal(EntityEntry dependent, IReadOnlyForeignKey foreignKey)
        {
            var values = new object?[foreignKey.Properties.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var value = dependent.Property(foreignKey.Properties[i].Name).CurrentValue;
                if (value is null)
                    return null;
                values[i] = value;
            }

            return MapFor(foreignKey.PrincipalKey).GetValueOrDefault(new KeyValues(values));
        }

        private Dictionary<KeyValues, EntityEntry> MapFor(IReadOnlyKey key)
        {
            if (_byKey.TryGetValue(key, out var map))
                return map;

            map = [];
            foreach (var entry in entries)
            {
                if (!key.DeclaringEntityType.IsAssignableFrom(entry.Metadata))
                    continue;

                var values = new object?[key.Properties.Count];
                var complete = true;
                for (var i = 0; i < values.Length && complete; i++)
                {
                    values[i] = entry.Property(key.Properties[i].Name).CurrentValue;
                    complete = values[i] is not null;
                }

                if (complete)
                    map.TryAdd(new KeyValues(values), entry);
            }

            _byKey[key] = map;
            return map;
        }
    }

    private readonly struct KeyValues(object?[] values) : IEquatable<KeyValues>
    {
        private readonly object?[] _values = values;

        public bool Equals(KeyValues other)
        {
            if (_values.Length != other._values.Length)
                return false;

            for (var i = 0; i < _values.Length; i++)
            {
                if (!Equals(_values[i], other._values[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is KeyValues other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in _values)
                hash.Add(value);
            return hash.ToHashCode();
        }
    }
}
