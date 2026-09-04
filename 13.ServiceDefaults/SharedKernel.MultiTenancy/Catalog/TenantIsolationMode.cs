namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// Describes how a tenant's data is physically isolated from other tenants.
/// </summary>
/// <remarks>
/// A prose-only mirror of <c>06.Persistence</c>'s existing tenant-isolation vocabulary (row-level
/// filtering vs. DB-per-tenant/schema-per-tenant) — this enum introduces no new
/// <c>06.Persistence</c> reference; it is purely descriptive metadata surfaced on
/// <see cref="TenantDescriptor"/>.
/// </remarks>
public enum TenantIsolationMode
{
    /// <summary>
    /// The tenant's rows are co-located with other tenants' in shared tables, distinguished by a
    /// tenant-id column and a row-level filter (e.g. <c>06.Persistence</c>'s <c>TenantedDbContext</c>).
    /// </summary>
    Shared,

    /// <summary>
    /// The tenant has a dedicated database or schema — e.g. the DB-per-tenant/schema-per-tenant
    /// isolation model <see cref="Resolution.DatabaseTenantResolutionStrategy"/> is designed for.
    /// </summary>
    Dedicated,
}
