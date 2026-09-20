namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// An explicit, auditable escape hatch that lets a caller deliberately bypass tenant isolation
/// (the global tenant query filter, and — from a later phase — the tenant write guard) for a
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
/// A later phase (W2) wires this into <c>SpecificationEvaluator{T}</c>'s cross-tenant
/// <c>IgnoreQueryFilters</c> gate and a new tenant write guard so a query/write against another
/// tenant's rows only succeeds while a scope is active. This phase only ships the seam and a default
/// implementation — no enforcement yet.
/// </para>
/// </remarks>
public interface ICrossTenantScope
{
    /// <summary>Gets whether a cross-tenant bypass is active for the current logical call.</summary>
    bool IsActive { get; }
}
