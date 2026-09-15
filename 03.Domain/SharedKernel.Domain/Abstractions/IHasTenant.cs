namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record that belongs to exactly one tenant.
/// </summary>
/// <remarks>
/// <b>Usage.</b> The application layer supplies the tenant from its resolved tenant context when the
/// record is created; the domain never resolves tenants. The persistence layer reads this interface to
/// map and scope tenant data.
/// </remarks>
public interface IHasTenant
{
    /// <summary>Gets the identifier of the tenant that owns the record.</summary>
    Guid TenantId { get; }
}
