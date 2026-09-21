namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// An explicit, auditable escape hatch that lets a caller deliberately bypass tenant isolation for a
/// bounded scope, e.g. an admin cross-tenant report, a data-migration job or a startup seeder.
/// </summary>
/// <remarks>
/// <para>
/// Absence of an active scope is the default, safe state. A bypass is entered with
/// <see cref="Enter(string)"/>, which requires a reason and records the calling actor (taken from the
/// current <c>IRequestContext</c>), so every bypass is attributable in logs and metrics:
/// <code>
/// using (crossTenantScope.Enter("monthly revenue report across all tenants"))
/// {
///     var rows = await db.Invoices.IgnoreQueryFilters([PersistenceFilterNames.Tenant]).ToListAsync(ct);
/// }
/// </code>
/// </para>
/// <para>
/// <strong>Flow, not instance.</strong> The active state belongs to the current logical call flow
/// (<see cref="System.Threading.AsyncLocal{T}"/>), not to the instance that entered it: every
/// <see cref="ICrossTenantScope"/> in the same flow reports the same <see cref="IsActive"/>. Enter and
/// dispose in the same method (a <see langword="using"/> block); a scope entered inside a called
/// <see langword="async"/> method does not flow back to its caller.
/// </para>
/// <para>
/// <strong>Enforcement:</strong> honored by the EF Core tenant write guard, the specification evaluator's
/// cross-tenant query gate, <c>TenantedRepository.GetByIdForTenantAsync</c>, the tenant-safe Dapper
/// services, the audit query service and, when enabled, PostgreSQL row-level security binding. Outside
/// an active scope each of these fails closed.
/// </para>
/// <para>
/// <strong>Registration:</strong> scoped, because the actor comes from the scope's <c>IRequestContext</c>.
/// <c>AddSharedKernelPostgres</c> registers it; a Dapper-only service calls
/// <c>services.AddSharedKernelCrossTenantScope()</c>. Singleton infrastructure that only needs to know
/// whether a bypass is active reads <see cref="CrossTenantScope.IsActiveInCurrentFlow"/> instead of
/// injecting this scoped service.
/// </para>
/// </remarks>
public interface ICrossTenantScope
{
    /// <summary>Gets whether a cross-tenant bypass is active for the current logical call flow.</summary>
    bool IsActive { get; }

    /// <summary>Activates the cross-tenant bypass until the returned handle is disposed.</summary>
    /// <param name="reason">
    /// Why tenant isolation is bypassed (e.g. <c>"monthly revenue report"</c>). Required; written to the
    /// log together with the calling actor. Never put personal data in it.
    /// </param>
    /// <returns>A handle that deactivates this entry when disposed. Disposing twice is harmless.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null, empty or whitespace.</exception>
    /// <remarks>
    /// Nested calls compose: the bypass stays active until every entry has been disposed. Every call is
    /// logged and counted, nested ones included, because each call site is an independent decision.
    /// </remarks>
    IDisposable Enter(string reason);
}
