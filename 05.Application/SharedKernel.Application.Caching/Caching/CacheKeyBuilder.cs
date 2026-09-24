using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Caching;

/// <summary>
/// Builds the key a query reads and the key the invalidating command removes, from the same inputs,
/// so the two can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// Every key goes through <see cref="ITenantCacheKeyProvider"/> rather than string interpolation, so
/// it carries the owning service's name, every part is escaped, and a tenant key can never equal a
/// global one. This replaces the earlier hand-built form, which passed a raw caller-supplied string
/// straight to <see cref="ICacheService"/> and reached for the tenant <i>tag</i> builder to produce a
/// <i>key</i>.
/// </para>
/// <para>
/// Fails closed: when the declared <see cref="CacheScope"/>'s identity is missing, no key is produced
/// and the caller runs the handler uncached rather than falling back to a wider scope.
/// </para>
/// </remarks>
internal static class CacheKeyBuilder
{
    /// <summary>
    /// Builds the key for one query or invalidation target, or reports that the declared scope's
    /// identity is unavailable.
    /// </summary>
    internal static bool TryBuild(
        ITenantCacheKeyProvider keys,
        CacheScope scope,
        string entity,
        string id,
        Guid? tenantId,
        string? userId,
        out string key)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        switch (scope)
        {
            case CacheScope.Global:
                key = keys.BuildKey(entity, id);
                return true;

            case CacheScope.Tenant when tenantId is { } tenant:
                key = keys.BuildTenantKey(TenantSegment(tenant), entity, id);
                return true;

            case CacheScope.User when !string.IsNullOrWhiteSpace(userId):
                key = tenantId is { } userTenant
                    ? keys.BuildTenantKey(TenantSegment(userTenant), entity, id, UserSegmentMarker, userId)
                    : keys.BuildKey(entity, id, UserSegmentMarker, userId);
                return true;

            case CacheScope.Tenant:
            case CacheScope.User:
                key = string.Empty;
                return false;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scope),
                    scope,
                    $"'{scope}' is not a defined {nameof(CacheScope)} value. Use one of: "
                        + string.Join(", ", Enum.GetNames<CacheScope>()) + ".");
        }
    }

    /// <summary>Builds the tag a query attaches and the invalidating command removes.</summary>
    /// <remarks>
    /// Tags are not keys: <see cref="CacheKeyFormat.BuildTenantTag"/> is the tenant tag form, and a
    /// global tag is used verbatim. A caller-supplied global tag must not start with the tenant
    /// marker, or it would address a tenant's whole namespace.
    /// </remarks>
    internal static bool TryBuildTag(
        CacheScope scope,
        string tag,
        Guid? tenantId,
        string? userId,
        out string scopedTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        switch (scope)
        {
            case CacheScope.Global when tag[0] == CacheKeyFormat.TenantMarker:
                throw new ArgumentException(
                    $"Cache tag '{tag}' starts with '{CacheKeyFormat.TenantMarker}', which is reserved for tenant-scoped tags.",
                    nameof(tag));

            case CacheScope.Global:
                scopedTag = tag;
                return true;

            case CacheScope.Tenant when tenantId is { } tenant:
                scopedTag = CacheKeyFormat.BuildTenantTag(TenantSegment(tenant), tag);
                return true;

            // A user-scoped command still evicts at tenant granularity: a tag names a set of entries,
            // and the set a command invalidates is not narrowed by which caller issued it.
            case CacheScope.User when tenantId is { } userTenant:
                scopedTag = CacheKeyFormat.BuildTenantTag(TenantSegment(userTenant), tag);
                return true;

            case CacheScope.User when !string.IsNullOrWhiteSpace(userId):
                scopedTag = tag;
                return true;

            case CacheScope.Tenant:
            case CacheScope.User:
                scopedTag = string.Empty;
                return false;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scope),
                    scope,
                    $"'{scope}' is not a defined {nameof(CacheScope)} value. Use one of: "
                        + string.Join(", ", Enum.GetNames<CacheScope>()) + ".");
        }
    }

    /// <summary>Applies the tenant to the policy, so the entry also carries the tenant-wide tag.</summary>
    internal static CachePolicy Policy(CacheScope scope, CachePolicy policy, Guid? tenantId)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return scope is not CacheScope.Global && tenantId is { } tenant
            ? policy.ForTenant(TenantSegment(tenant))
            : policy;
    }

    private const string UserSegmentMarker = "u";

    private static string TenantSegment(Guid tenantId) => tenantId.ToString("D");
}
