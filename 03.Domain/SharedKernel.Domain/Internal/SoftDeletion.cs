using SharedKernel.Guards;

namespace SharedKernel.Domain.Internal;

/// <summary>
/// The soft-delete transition rule shared by every soft-deletable base class. The properties have to
/// be declared on each class for the ORM to map them; the rule that governs them lives only here.
/// </summary>
internal static class SoftDeletion
{
    /// <summary>
    /// Throws <c>DomainException</c> when <paramref name="deletedBy"/> is null or whitespace, then returns
    /// whether the record should transition to deleted. A record that is already deleted stays as it is, so
    /// deleting twice keeps the original timestamp and actor and raises no second event.
    /// </summary>
    internal static bool ShouldMarkDeleted(bool isDeleted, string deletedBy)
    {
        Guard.Throw.NullOrWhiteSpace(deletedBy, nameof(deletedBy));
        return !isDeleted;
    }
}
