using SharedKernel.Primitives.Errors;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Builds the single, shared <see cref="Error"/> shape a tenant-isolation write rejection carries,
/// regardless of which of the two independent enforcement points raised it.
/// </summary>
/// <remarks>
/// <see cref="TenantWriteGuardInterceptor"/> raises this proactively, in memory, before a save is
/// attempted. <see cref="ConcurrencyInterceptor"/> raises the same error reactively when a write's
/// database-level <c>TenantId</c> concurrency-token check (see
/// <c>MultiTenancy.TenantedDbContext.ApplyTenantFilters</c>) affects zero rows — the case a detached
/// entity's in-memory check cannot see: a caller-supplied stub whose claimed <c>TenantId</c> matches
/// their own tenant but whose primary key targets a row belonging to a different one. A caller of
/// either path receives the identical error code and message shape.
/// </remarks>
internal static class TenantIsolationErrors
{
    /// <summary>Builds the tenant-isolation-violation <see cref="Error"/> for <paramref name="entityTypeName"/>.</summary>
    /// <param name="entityTypeName">The CLR type name of the rejected entity. Never the row payload.</param>
    /// <param name="reason">A short clause completing "was {reason} the current tenant".</param>
    public static Error Build(string entityTypeName, string reason) =>
        Error.Forbidden(
            "persistence.tenant_isolation_violation",
            $"A write was rejected: '{entityTypeName}' was {reason} the current tenant. " +
            "Enter an ICrossTenantScope explicitly if this write is a deliberate cross-tenant operation.");
}
