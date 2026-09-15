namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record that tracks who created it and when.
/// </summary>
/// <remarks>
/// <b>Persistence.</b> The persistence layer writes <see cref="CreatedBy"/> and <see cref="CreatedOn"/>
/// when a new record is first saved; domain code never sets them. Before that save they hold their
/// default values.
/// </remarks>
public interface IHasCreatedAudit
{
    /// <summary>
    /// Gets the actor that created the record, typically a user identifier, or a service name when no
    /// user is authenticated.
    /// </summary>
    string CreatedBy { get; }

    /// <summary>Gets the UTC time the record was first saved.</summary>
    DateTimeOffset CreatedOn { get; }
}
