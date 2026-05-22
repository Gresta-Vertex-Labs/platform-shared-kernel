namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as having creation audit metadata.
/// The <see cref="CreatedBy"/> and <see cref="CreatedOn"/> properties are populated exclusively
/// by EF Core interceptors or persistence-layer conventions — the domain never writes them.
/// </summary>
public interface IHasCreatedAudit
{
    /// <summary>Gets the identifier of the actor who created this entity.</summary>
    string CreatedBy { get; }

    /// <summary>Gets the UTC timestamp at which this entity was created.</summary>
    DateTimeOffset CreatedOn { get; }
}
