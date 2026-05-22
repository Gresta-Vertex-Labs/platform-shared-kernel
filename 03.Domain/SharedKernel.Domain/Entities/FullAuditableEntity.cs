using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// Abstract entity base implementing <see cref="IHasAudit"/>, <see cref="ISoftDeletable"/>,
/// and <see cref="IHasConcurrency"/> — the full audit stack for non-aggregate child entities.
/// </summary>
/// <typeparam name="TId">The type of the entity's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// All audit and soft-delete fields have <c>private set</c> — populated exclusively by
/// EF Core interceptors or persistence-layer conventions.
/// </para>
/// <para>
/// <see cref="RowVersion"/> has <c>protected set</c> so the persistence layer can populate
/// it after fetch without EF Core shadow properties.
/// </para>
/// <para>
/// Unlike <c>SoftDeletableAggregateRoot</c>, this entity base does not provide a
/// <c>MarkAsDeleted</c> helper — soft-delete on a non-aggregate entity should be driven
/// by the owning aggregate root.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class AttachmentId(Guid Value) : StronglyTypedId&lt;Guid&gt;(Value);
///
/// public sealed class Attachment : FullAuditableEntity&lt;AttachmentId&gt;
/// {
///     public string FileName { get; private set; }
///
///     public Attachment(AttachmentId id, string fileName) : base(id)
///     {
///         FileName = fileName;
///     }
///
///     protected Attachment() { } // ORM path
/// }
/// </code>
/// </example>
public abstract class FullAuditableEntity<TId> : Entity<TId>, IHasAudit, ISoftDeletable, IHasConcurrency
    where TId : notnull
{
    /// <summary>
    /// Initialises a new full-auditable entity with the specified identity key.
    /// </summary>
    protected FullAuditableEntity(TId id) : base(id) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected FullAuditableEntity() : base() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }

    /// <inheritdoc/>
    public bool IsDeleted { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedOn { get; private set; }

    /// <inheritdoc/>
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Gets or sets the opaque concurrency token.
    /// <c>protected set</c> allows the persistence layer to populate this after a fetch.
    /// </summary>
    public byte[] RowVersion { get; protected set; } = [];
}
