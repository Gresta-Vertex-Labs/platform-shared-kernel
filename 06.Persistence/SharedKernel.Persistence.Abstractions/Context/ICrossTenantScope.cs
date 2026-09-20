namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// An explicit, auditable escape hatch that lets a caller deliberately bypass tenant isolation for a
/// bounded scope, e.g. an admin cross-tenant report or a data-migration job.
/// </summary>
/// <remarks>
/// <para>
/// Absence of an active scope is the default, safe state — every bypass must be explicit
/// and attributable to the code that requested it. This interface only exposes the read side
/// (<see cref="IsActive"/>); entering a scope is a separate, deliberately narrower capability — see
/// <see cref="Context.CrossTenantScope"/>'s <c>Enter</c> method.
/// </para>
/// <para>
/// <strong>Enforcement (shipped):</strong> an active scope is honored by
/// <c>SpecificationEvaluator{T}</c>'s cross-tenant <c>IgnoreQueryFilters</c> gate,
/// <c>TenantWriteGuardInterceptor</c>, <c>TenantedRepository.GetByIdForTenantAsync</c>, the tenant-safe
/// Dapper read/command services, and — for services opted into row-level security — the PostgreSQL RLS
/// escape clause <c>ITenantSessionBinder</c> binds to the database session. A query or write against
/// another tenant's rows only succeeds while a scope is active; outside one, every path above fails
/// closed exactly as if no bypass existed.
/// </para>
/// <para>
/// <strong>Auditability:</strong> every call to <see cref="Context.CrossTenantScope"/>'s <c>Enter</c>
/// method is recorded on the <c>"SharedKernel.Persistence"</c> <see cref="System.Diagnostics.Metrics.Meter"/>,
/// tagged with the actor id the caller supplied — see <c>Enter(string?)</c>'s own remarks.
/// </para>
/// </remarks>
public interface ICrossTenantScope
{
    /// <summary>Gets whether a cross-tenant bypass is active for the current logical call.</summary>
    bool IsActive { get; }
}
