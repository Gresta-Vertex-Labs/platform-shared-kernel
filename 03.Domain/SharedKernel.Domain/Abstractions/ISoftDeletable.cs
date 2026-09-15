namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record that supports soft delete: it is marked deleted and kept, rather than removed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Domain code soft-deletes through the base classes' <c>MarkAsDeleted</c> method. The
/// persistence layer also sets these values when a tracked record is removed, turning the delete into
/// an update.
/// </para>
/// <para>
/// <b>Pitfall.</b> A soft delete performed by the persistence layer raises no domain event; delete
/// through a domain method whenever other parts of the system must react.
/// </para>
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>Gets whether the record has been soft-deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>
    /// Gets the UTC time of the soft delete, or <see langword="null"/> when the record is not deleted.
    /// </summary>
    DateTimeOffset? DeletedOn { get; }

    /// <summary>
    /// Gets the actor that soft-deleted the record, or <see langword="null"/> when the record is not deleted.
    /// </summary>
    string? DeletedBy { get; }
}
