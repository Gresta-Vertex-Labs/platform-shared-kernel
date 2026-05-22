namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Zero-member marker interface indicating that a type belongs to a specific tenant.
/// The <see cref="TenantId"/> property is set by the persistence layer or at creation time;
/// the domain layer has no tenant resolution logic.
/// </summary>
public interface IHasTenant
{
    /// <summary>Gets the unique identifier of the tenant that owns this record.</summary>
    Guid TenantId { get; }
}
