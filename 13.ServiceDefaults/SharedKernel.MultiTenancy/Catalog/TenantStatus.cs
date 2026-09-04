namespace SharedKernel.MultiTenancy.Catalog;

/// <summary>
/// The lifecycle status of a tenant, as recorded in a consuming service's own tenant-management
/// system of record.
/// </summary>
public enum TenantStatus
{
    /// <summary>The tenant is active and permitted to serve requests.</summary>
    Active,

    /// <summary>
    /// The tenant is temporarily suspended (e.g. billing dispute, compliance hold). Requests for
    /// this tenant must be rejected until it is reactivated.
    /// </summary>
    Suspended,

    /// <summary>
    /// The tenant has been permanently offboarded. Requests for this tenant must be rejected;
    /// unlike <see cref="Suspended"/>, this is not expected to be reversed.
    /// </summary>
    Offboarded,
}
