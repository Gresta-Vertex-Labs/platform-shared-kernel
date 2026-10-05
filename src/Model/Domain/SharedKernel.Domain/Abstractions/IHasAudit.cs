namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A record that tracks who last modified it and when, in addition to who created it and when.
/// </summary>
/// <remarks>
/// <b>Persistence.</b> The persistence layer writes the modification values each time a changed record
/// is saved; domain code never sets them. Saving a new record sets only the creation values.
/// </remarks>
public interface IHasAudit : IHasCreatedAudit
{
    /// <summary>
    /// Gets the actor that last modified the record, or <see langword="null"/> when it has not been
    /// modified since it was created.
    /// </summary>
    string? ModifiedBy { get; }

    /// <summary>
    /// Gets the UTC time of the last modification, or <see langword="null"/> when the record has not been
    /// modified since it was created.
    /// </summary>
    DateTimeOffset? ModifiedOn { get; }
}
