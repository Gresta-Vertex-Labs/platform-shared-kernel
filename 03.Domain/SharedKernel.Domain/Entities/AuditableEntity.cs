using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// Abstract entity base implementing <see cref="IHasAudit"/>, adding creation and
/// last-modification audit metadata to a plain entity (no event machinery).
/// </summary>
/// <typeparam name="TId">The type of the entity's identity key. Must be non-null.</typeparam>
/// <remarks>
/// Audit properties have <c>private set</c> — populated exclusively by EF Core interceptors
/// or persistence-layer conventions. Domain code must not write them.
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderLineId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
///
/// public sealed class OrderLine : AuditableEntity&lt;OrderLineId&gt;
/// {
///     public int Quantity { get; private set; }
///
///     public OrderLine(OrderLineId id, int quantity) : base(id)
///     {
///         Quantity = quantity;
///     }
///
///     protected OrderLine() { } // ORM path
/// }
/// </code>
/// </example>
public abstract class AuditableEntity<TId> : Entity<TId>, IHasAudit
    where TId : notnull
{
    /// <summary>
    /// Initialises a new auditable entity with the specified identity key.
    /// </summary>
    protected AuditableEntity(TId id) : base(id) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected AuditableEntity() : base() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }
}
