using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Keyset/cursor-paginated query for everything a single actor has done, newest-or-oldest first.
/// </summary>
/// <remarks>
/// WO-071/P-456/D-118. Extends <see cref="KeysetSpecification{T,TKey}"/> (P-308/WO-051) rather than
/// inventing a third paging model. Sorts by <see cref="AuditRecord.OccurredOn"/> with
/// <see cref="AuditRecord.Id"/> as the mandatory tiebreaker, inherited automatically from the base
/// constructor.
/// </remarks>
public sealed class AuditActorActionsSpecification : KeysetSpecification<AuditRecord, DateTimeOffset>
{
    /// <summary>
    /// Initialises a new actor-actions query.
    /// </summary>
    /// <param name="tenantId">The tenant to scope the query to.</param>
    /// <param name="actorId">The actor identifier to filter by (see <see cref="AuditRecord.ActorId"/>'s format).</param>
    /// <param name="afterKey">The <see cref="AuditRecord.OccurredOn"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="afterId">The <see cref="AuditRecord.Id"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="descending">
    /// <see langword="true"/> to return the most recent records first; <see langword="false"/> for
    /// chronological (oldest-first) order.
    /// </param>
    /// <param name="take">The maximum number of records to return. Must be at least 1.</param>
    public AuditActorActionsSpecification(
        Guid tenantId,
        string actorId,
        DateTimeOffset? afterKey,
        Guid? afterId,
        bool descending,
        int take)
        : base(r => r.OccurredOn, r => r.Id, afterKey, afterId, descending, take)
    {
        AddCriteria(r => r.TenantId == tenantId && r.ActorId == actorId);
    }
}
