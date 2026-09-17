using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// The tenant scoping shared by <see cref="CachingBehavior{TRequest,TResponse}"/> and
/// <c>CacheInvalidationBehavior</c>, so a key or tag written by one is exactly what the other removes.
/// </summary>
internal static class CacheScope
{
    // A tenant-scoped key uses the tenant tag format, @{tenant}:{key}, with both parts escaped.
    internal static string Key(Guid? tenantId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (tenantId is { } tenant)
            return CacheKeyFormat.BuildTenantTag(tenant.ToString("D"), key);

        if (key[0] == CacheKeyFormat.TenantMarker)
        {
            throw new ArgumentException(
                $"Cache key '{key}' starts with '{CacheKeyFormat.TenantMarker}', which is reserved for tenant-scoped keys.",
                nameof(key));
        }

        return key;
    }

    internal static string Tag(Guid? tenantId, string tag) =>
        tenantId is { } tenant ? CacheKeyFormat.BuildTenantTag(tenant.ToString("D"), tag) : tag;

    internal static CachePolicy Policy(Guid? tenantId, CachePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return tenantId is { } tenant ? policy.ForTenant(tenant.ToString("D")) : policy;
    }
}
