using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Keyset/cursor-paginated query for everything a single actor has done within one tenant, newest-or-oldest first.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="KeysetSpecification{T,TKey}"/>. Kept
/// <see cref="AuditRecord.OccurredOn"/>-ordered (with <see cref="AuditRecord.Id"/> as tiebreaker) rather
/// than <see cref="AuditRecord.Sequence"/>-ordered — unlike <see cref="AuditResourceHistorySpecification"/>,
/// this query spans EVERY resource type the actor touched, i.e. potentially many chains at once, so
/// <see cref="AuditRecord.Sequence"/> (unique only within one chain) is not a meaningful cross-chain
/// sort key. This is a display/reporting order, not a chain-verification order — verification always
/// operates within a single chain via <see cref="IAuditQueryService.VerifyFullChainAsync"/>.
/// </para>
/// <para>
/// <strong>Deliberately carries no tenant parameter</strong> — see <see cref="AuditResourceHistorySpecification"/>'s
/// remarks for why; the same reasoning applies here.
/// </para>
/// </remarks>
public sealed class AuditActorActionsSpecification : KeysetSpecification<AuditRecord, DateTimeOffset>
{
    /// <summary>
    /// Initialises a new actor-actions query, scoped to whichever tenant
    /// <see cref="IAuditQueryService.GetActorActionsAsync"/> resolves.
    /// </summary>
    /// <param name="actorId">The actor identifier to filter by (see <see cref="AuditRecord.ActorId"/>'s format).</param>
    /// <param name="afterKey">The <see cref="AuditRecord.OccurredOn"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="afterId">The <see cref="AuditRecord.Id"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="descending">
    /// <see langword="true"/> to return the most recent records first; <see langword="false"/> for
    /// chronological (oldest-first) order.
    /// </param>
    /// <param name="take">The maximum number of records to return. Must be between 1 and <see cref="AuditQueryLimits.MaxPageSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is below 1 or above <see cref="AuditQueryLimits.MaxPageSize"/>.</exception>
    public AuditActorActionsSpecification(
        string actorId,
        DateTimeOffset? afterKey,
        Guid? afterId,
        bool descending,
        int take)
            : base(r => r.OccurredOn, r => r.Id, afterKey, afterId, descending, ClampTake(take))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        AddCriteria(r => r.ActorId == actorId);
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
