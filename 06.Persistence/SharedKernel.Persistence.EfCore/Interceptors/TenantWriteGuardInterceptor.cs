using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// EF Core save-changes interceptor that rejects any tracked write to an <see cref="IHasTenant"/>
/// entity outside the current tenant, unless an <see cref="ICrossTenantScope"/> bypass is active.
/// </summary>
/// <remarks>
/// <para>
/// Registered only for multi-tenant services (<c>EfCorePersistenceBuilder.WithMultiTenancy()</c>).
/// The global tenant query filter (<see cref="MultiTenancy.TenantedDbContext"/>) already stops a
/// cross-tenant row from being SELECTed in the first place, but nothing previously stopped an
/// already-tracked or manually-attached entity — e.g. a <see langword="detached"/> entity
/// <c>EfRepository.MarkAsModifiedIfDetached</c> re-attaches via <c>Update()</c> — from being written
/// with the wrong <c>TenantId</c>, or from having its <c>TenantId</c> itself silently changed. This
/// interceptor is the write-side half of tenant isolation; the query filter is the read-side half.
/// </para>
/// <para>
/// <strong>Checked for every <see cref="EntityState.Added"/>, <see cref="EntityState.Modified"/>, and
/// <see cref="EntityState.Deleted"/> entry implementing <see cref="IHasTenant"/>:</strong>
/// </para>
/// <list type="bullet">
/// <item><description>
/// The entry's current <c>TenantId</c> must equal <see cref="ICurrentTenantContext.TenantId"/>.
/// A <see langword="null"/> current tenant (no tenant resolved) rejects every tenant-scoped write —
/// fail closed, mirroring the read-side query filter's zero-row behavior.
/// </description></item>
/// <item><description>
/// For <see cref="EntityState.Modified"/> entries, the <c>TenantId</c> property itself must not be
/// modified (<c>entry.Property(...).IsModified</c>) — a row can never be moved between tenants.
/// </description></item>
/// </list>
/// <para>
/// <strong>Explicit bypass:</strong> when <see cref="ICrossTenantScope.IsActive"/> is
/// <see langword="true"/> for the current logical call (entered via
/// <c>ICrossTenantScope.Enter()</c>), every check above is skipped — an admin/migration code path
/// that deliberately opted in to cross-tenant access via the one sanctioned, auditable escape hatch.
/// </para>
/// <para>
/// <strong>Pooling-safe:</strong> unlike its earlier design, this
/// interceptor no longer captures <see cref="ICurrentTenantContext"/> in its own constructor — it
/// reads tenant identity LIVE off <c>eventData.Context</c> (cast to <see cref="TenantedDbContext"/>),
/// mirroring how <c>AuditInterceptor</c>/<c>SoftDeleteInterceptor</c> already read
/// <c>SharedKernelDbContext.CurrentActor</c> live rather than from a constructor-captured field. Its
/// only constructor dependency, <see cref="ICrossTenantScope"/>, is <see cref="AsyncLocal{T}"/>-backed
/// and registered singleton — safe to resolve from any provider, including EF Core's pooled-context
/// activator. This is what lets <c>EfCorePersistenceBuilder.WithMultiTenancy()</c> register this
/// interceptor SINGLETON under <c>.WithDbContextPooling()</c>, exactly like the three platform
/// interceptors.
/// </para>
/// </remarks>
public sealed class TenantWriteGuardInterceptor : SaveChangesInterceptor
{
    private readonly ICrossTenantScope _crossTenantScope;

    /// <summary>Initialises a new <see cref="TenantWriteGuardInterceptor"/>.</summary>
    /// <param name="crossTenantScope">Scoped/singleton DI dependency reporting an active bypass.</param>
    public TenantWriteGuardInterceptor(ICrossTenantScope crossTenantScope)
    {
        _crossTenantScope = crossTenantScope;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        EnforceTenantIsolation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantIsolation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceTenantIsolation(DbContext? context)
    {
        if (context is null || _crossTenantScope.IsActive)
            return;

        // Read LIVE off the executing context instance — never a constructor-captured
        // field — so a pooled instance always sees the identity attached for the lease actually
        // performing this save, not whichever scope happened to construct the pool slot. A context
        // that is somehow not a TenantedDbContext (should never happen in practice — only
        // TenantedDbContext subclasses register this interceptor) fails closed to "no tenant".
        var currentTenantId = (context as TenantedDbContext)?.CurrentTenant.TenantId;

        foreach (var entry in context.ChangeTracker.Entries<IHasTenant>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // Never rely on
            // Property(...).IsModified here. SoftDeleteInterceptor and AggregateRootTouchInterceptor
            // both flip an entry's State DIRECTLY (Deleted -> Modified, Unchanged -> Modified) rather
            // than through normal property-level change detection — EF Core's own documented
            // behavior for a state set this way is to mark EVERY scalar property "modified" relative
            // to its ORIGINAL snapshot, regardless of whether its value actually changed. Checking
            // IsModified after either of those interceptors has already run (both fire before this
            // one — see SharedKernelDbContext.OnConfiguring's ordering) produced a false positive on
            // every soft-delete/touch of a tenanted entity. Comparing the actual current vs. original
            // TenantId VALUE is correct regardless of how the state transition happened.
            if (entry.State == EntityState.Modified
                && !Equals(
                    entry.Property(nameof(IHasTenant.TenantId)).CurrentValue,
                    entry.Property(nameof(IHasTenant.TenantId)).OriginalValue))
            {
                RecordViolation(entry.Entity.GetType().Name);
                throw new ForbiddenException(BuildError(entry.Entity.GetType().Name, "changed the TenantId of"));
            }

            if (currentTenantId is null || entry.Entity.TenantId != currentTenantId.Value)
            {
                RecordViolation(entry.Entity.GetType().Name);
                throw new ForbiddenException(BuildError(entry.Entity.GetType().Name, "written outside the current tenant for"));
            }
        }
    }

    private static void RecordViolation(string entityTypeName) =>
        Diagnostics.PersistenceMeter.TenantIsolationViolations.Add(1,
            new KeyValuePair<string, object?>(Diagnostics.PersistenceTagKeys.AggregateType, entityTypeName));

    private static Error BuildError(string entityTypeName, string reason) =>
        TenantIsolationErrors.Build(entityTypeName, reason);
}
