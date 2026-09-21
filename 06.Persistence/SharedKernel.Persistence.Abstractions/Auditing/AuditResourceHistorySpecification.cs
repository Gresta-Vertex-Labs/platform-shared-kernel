using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Keyset/cursor-paginated query for a single resource's audit history within one chain, newest-or-oldest first.
/// </summary>
/// <remarks>
/// <para>
/// A filter plus a keyset cursor, paged by the query service and keyed by
/// <see cref="AuditRecord.Sequence"/> rather than <see cref="AuditRecord.OccurredOn"/> — see
/// <see cref="AuditRecord"/>'s remarks for why sequence, not wall-clock time, orders a chain.
/// <see cref="AuditRecord.Id"/> remains the base class's mandatory tiebreaker even though
/// <see cref="AuditRecord.Sequence"/> is already unique within one chain.
/// </para>
/// <para>
/// <strong>Deliberately carries no tenant parameter.</strong> Tenant scoping is applied by
/// <see cref="IAuditQueryService.GetResourceHistoryAsync"/> itself, resolved from the caller's own
/// <c>IRequestContext</c> — never from a value this publicly-constructible type could be handed
/// a forged tenant id through. See <see cref="IAuditQueryService"/>'s remarks.
/// </para>
/// </remarks>
public sealed class AuditResourceHistorySpecification : Specification<AuditRecord>
{
    /// <summary>Gets the sequence of the last row of the previous page, or <see langword="null"/> on the first page.</summary>
    public long? AfterKey { get; }

    /// <summary>Gets the id of the last row of the previous page, or <see langword="null"/> on the first page.</summary>
    public Guid? AfterId { get; }

    /// <summary>Gets a value indicating whether the most recent records come first.</summary>
    public bool Descending { get; }

    /// <summary>Gets the page size.</summary>
    public int PageSize { get; }

    /// <summary>
    /// Initialises a new resource-history query, scoped to a single resource instance within
    /// whichever tenant's chain <see cref="IAuditQueryService.GetResourceHistoryAsync"/> resolves.
    /// </summary>
    /// <param name="resourceType">The resource type to filter by.</param>
    /// <param name="resourceId">The specific resource instance to filter by.</param>
    /// <param name="afterSequence">The <see cref="AuditRecord.Sequence"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="afterId">The <see cref="AuditRecord.Id"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="descending">
    /// <see langword="true"/> to return the most recent records first; <see langword="false"/> for
    /// chronological (oldest-first) order.
    /// </param>
    /// <param name="take">The maximum number of records to return. Must be between 1 and <see cref="AuditQueryLimits.MaxPageSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is below 1 or above <see cref="AuditQueryLimits.MaxPageSize"/>.</exception>
    public AuditResourceHistorySpecification(
        string resourceType,
        string resourceId,
        long? afterSequence,
        Guid? afterId,
        bool descending,
        int take)
    {
        if (afterSequence.HasValue != afterId.HasValue)
            throw new ArgumentException("A cursor supplies both afterSequence and afterId, or neither.", nameof(afterSequence));

        AfterKey = afterSequence;
        AfterId = afterId;
        Descending = descending;
        PageSize = ClampTake(take);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        AddCriteria(r => r.ResourceType == resourceType && r.ResourceId == resourceId);
    }

    private static int ClampTake(int take)
    {
        if (take is < 1 or > AuditQueryLimits.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take), take,
                $"Take must be between 1 and {AuditQueryLimits.MaxPageSize} (AuditQueryLimits.MaxPageSize).");
        }

        return take;
    }
}
