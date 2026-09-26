using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Builds tenant cache keys in the format defined by <see cref="CacheKeyFormat"/>, for callers that
/// need a raw tenant key. <see cref="ITenantCacheService"/> covers ordinary reads and writes.
/// </summary>
/// <remarks>
/// The tenant identifier is always an explicit argument, never resolved from ambient state. Two
/// calls that differ only in tenant always produce different keys, and a tenant key never equals a
/// key from <see cref="ICacheKeyProvider.BuildKey"/>.
/// </remarks>
public interface ITenantCacheKeyProvider : ICacheKeyProvider
{
    /// <summary>Builds a key in the format <c>{service}:@{tenant}:{entity}:{id}[:{segment}...]</c>.</summary>
    /// <param name="tenantId">The tenant identifier. Must not be <see langword="default"/>.</param>
    /// <param name="entity">The entity or resource name. Must not be null or whitespace.</param>
    /// <param name="id">The entity identifier. Must not be null or whitespace.</param>
    /// <param name="segments">Optional extra parts, such as a locale. Each must not be null or whitespace.</param>
    /// <returns>The tenant key.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is <see langword="default"/>, or a part is null or whitespace.</exception>
    string BuildTenantKey(TenantId tenantId, string entity, string id, params string[] segments);
}
