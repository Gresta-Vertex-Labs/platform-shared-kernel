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
/// <strong>Scope, not call flow.</strong> The active state belongs to the dependency-injection scope (the
/// request, message or job): the container creates one instance per scope and every component of that scope
/// — repositories, the EF Core context, Dapper sessions, the audit services — observes it. An entry made
/// anywhere, including inside an awaited helper method, stays active for the whole scope until its handle is
/// disposed, and is never visible to another scope. Concurrent work inside one scope shares the bypass.
/// </para>
/// <para>
/// <strong>Enforcement:</strong> honored by the EF Core tenant write guard, <c>TenantedRepository.GetByIdForTenantAsync</c>,
/// the Dapper sessions, the audit query service and, when enabled, PostgreSQL row-level security. Outside
/// an active scope each of these fails closed.
/// </para>
/// <para>
/// <strong>Registration:</strong> scoped, because the actor comes from the scope's <c>IRequestContext</c>.
/// <c>AddSharedKernelPostgres</c> and <c>AddSharedKernelDapper</c> register it; any other service calls
/// <c>services.AddSharedKernelCrossTenantScope()</c>. A context created through <c>ICallerDbContextFactory</c>
/// (no request scope) carries its own, attributed to that explicit caller: <c>db.CrossTenantScope.Enter(reason)</c>.
/// </para>
/// </remarks>
public interface ICrossTenantScope
{
    /// <summary>Gets whether a cross-tenant bypass is active in this scope.</summary>
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
