namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Extends <see cref="IHasCreatedAudit"/> with last-modification audit metadata.
/// All properties are populated exclusively by EF Core interceptors or persistence-layer conventions.
/// </summary>
public interface IHasAudit : IHasCreatedAudit
{
    /// <summary>Gets the identifier of the actor who last modified this entity, or <see langword="null"/> if never modified.</summary>
    string? ModifiedBy { get; }

    /// <summary>Gets the UTC timestamp of the last modification, or <see langword="null"/> if never modified.</summary>
    DateTimeOffset? ModifiedOn { get; }
}
