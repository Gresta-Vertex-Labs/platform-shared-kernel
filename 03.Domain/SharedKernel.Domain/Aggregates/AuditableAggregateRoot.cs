using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract aggregate root that implements <see cref="IHasAudit"/>, adding creation and
/// last-modification audit metadata.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// Audit properties (<see cref="CreatedBy"/>, <see cref="CreatedOn"/>, <see cref="ModifiedBy"/>,
/// <see cref="ModifiedOn"/>) have <c>private set</c> — they are populated exclusively by
/// EF Core interceptors or persistence-layer conventions, never by domain code.
/// </remarks>
/// <example>
/// <code>
/// public sealed class Product : AuditableAggregateRoot&lt;ProductId&gt;
/// {
///     public string Name { get; private set; }
///
///     public Product(ProductId id, string name, IClock clock) : base(id, clock)
///     {
///         Name = name;
///     }
///
///     protected Product() { } // ORM path
/// }
/// </code>
/// </example>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit
    where TId : notnull
{
    /// <summary>
    /// Initialises a new auditable aggregate root with the specified identity key and clock.
    /// </summary>
    protected AuditableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected AuditableAggregateRoot() : base() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }
}
