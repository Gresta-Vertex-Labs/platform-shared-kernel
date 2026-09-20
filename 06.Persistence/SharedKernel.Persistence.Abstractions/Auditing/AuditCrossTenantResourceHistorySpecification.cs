using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Keyset/cursor-paginated query for a single resource's audit history ACROSS EVERY TENANT — the
/// privileged counterpart to <see cref="AuditResourceHistorySpecification"/>, used only by
/// <see cref="IAuditQueryService.GetResourceHistoryAcrossTenantsAsync"/>.
/// </summary>
/// <remarks>
/// Keyed by <see cref="AuditRecord.Id"/> alone (a <see cref="Guid.CreateVersion7()"/> value,
/// roughly time-ordered) rather than <see cref="AuditRecord.Sequence"/> — see
/// <see cref="IAuditQueryService.GetResourceHistoryAcrossTenantsAsync"/>'s remarks for why a
/// cross-tenant result set cannot be ordered by a per-chain sequence number.
/// </remarks>
public sealed class AuditCrossTenantResourceHistorySpecification : KeysetSpecification<AuditRecord, Guid>
{
    /// <summary>Initialises a new cross-tenant resource-history query.</summary>
    /// <param name="resourceType">The resource type to filter by.</param>
    /// <param name="resourceId">The specific resource instance to filter by.</param>
    /// <param name="afterId">The <see cref="AuditRecord.Id"/> of the last row seen on the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="descending">
    /// <see langword="true"/> to return the most recently created records first; <see langword="false"/> for creation order.
    /// </param>
    /// <param name="take">The maximum number of records to return. Must be between 1 and <see cref="AuditQueryLimits.MaxPageSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is below 1 or above <see cref="AuditQueryLimits.MaxPageSize"/>.</exception>
    public AuditCrossTenantResourceHistorySpecification(
        string resourceType,
        string resourceId,
        Guid? afterId,
        bool descending,
        int take)
            : base(r => r.Id, r => r.Id, afterId, afterId, descending, ClampTake(take))
    {
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
