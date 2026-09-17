namespace SharedKernel.Security.Abstractions;

/// <summary>Resolves the tenant of the current operation.</summary>
/// <remarks>
/// Scoped. The authentication packages register <see cref="UserContextTenantProvider"/>, which returns the tenant
/// the caller's credential asserts; a host that resolves tenants another way registers its own. Domain code never
/// references this interface: the application layer passes the tenant id to aggregates.
/// </remarks>
public interface ITenantProvider
{
    /// <summary>Gets the tenant id, or <see cref="Guid.Empty"/> when the operation has no tenant.</summary>
    Guid TenantId { get; }
}
