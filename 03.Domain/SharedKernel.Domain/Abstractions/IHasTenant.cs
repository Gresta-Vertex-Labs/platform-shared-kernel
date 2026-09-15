namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record that belongs to exactly one tenant.
/// The <see cref="TenantId"/> property is set by the persistence layer or at creation time;
/// the domain layer has no tenant resolution logic.
/// </summary>
public interface IHasTenant
{
    /// <summary>Gets the unique identifier of the tenant that owns this record.</summary>
    Guid TenantId { get; }
}
