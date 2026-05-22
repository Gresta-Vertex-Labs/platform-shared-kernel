namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as supporting soft deletion — records are logically removed but physically retained.
/// All properties are populated exclusively by the domain's <c>MarkAsDeleted</c> helper or
/// persistence-layer conventions.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>Gets a value indicating whether this record has been soft-deleted.</summary>
    bool IsDeleted { get; }

    /// <summary>Gets the UTC timestamp at which this record was soft-deleted, or <see langword="null"/> if not deleted.</summary>
    DateTimeOffset? DeletedOn { get; }

    /// <summary>Gets the identifier of the actor who deleted this record, or <see langword="null"/> if not deleted.</summary>
    string? DeletedBy { get; }
}
