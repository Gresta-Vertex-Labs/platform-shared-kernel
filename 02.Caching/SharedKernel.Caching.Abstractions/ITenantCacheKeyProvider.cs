namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Constructs multi-tenant cache keys using the platform-standard namespaced key format.
/// Extends <see cref="ICacheKeyProvider"/> with per-tenant key isolation.
/// </summary>
/// <remarks>
/// <para>
/// The tenant key format contract is:
/// <c>{service}:{tenant}:{entity}:{id}[:{extraSegment}...]</c>
/// where <c>{service}</c> is the owning service name, <c>{tenant}</c> is the explicit
/// tenant identifier, <c>{entity}</c> is the entity type, and <c>{id}</c> is the entity
/// identifier. Additional segments are appended with <c>:</c> separator.
/// </para>
/// <para>
/// Example outputs:
/// <list type="bullet">
///   <item><description><c>order-svc:tenant-a:invoice:42</c> (basic tenant key)</description></item>
///   <item><description><c>order-svc:tenant-b:invoice:42</c> (different tenant, same entity — isolated)</description></item>
///   <item><description><c>order-svc:tenant-a:invoice:42:en-GB</c> (extra segment)</description></item>
/// </list>
/// </para>
/// <para>
/// The <c>tenantId</c> is always passed as an explicit parameter — this interface never
/// resolves the tenant from <c>IHttpContextAccessor</c>, ambient state, or
/// <c>12.Security</c> packages. The caller is responsible for supplying the correct
/// tenant context at the call site.
/// </para>
/// <para>
/// <strong>Tenant isolation guarantee:</strong> two calls with the same <c>entity</c>
/// and <c>id</c> but different <c>tenantId</c> values always produce different keys,
/// preventing cross-tenant cache leakage.
/// </para>
/// <para>
/// Register via <c>AddTenantCacheKeyProvider()</c> on the <see cref="ICachingBuilder"/>
/// in <c>SharedKernel.Caching.FusionCache</c>. This registration is additive — it does
/// not replace the existing <see cref="ICacheKeyProvider"/> singleton.
/// </para>
/// </remarks>
public interface ITenantCacheKeyProvider : ICacheKeyProvider
{
    /// <summary>
    /// Builds a tenant-namespaced cache key in the format
    /// <c>{service}:{tenant}:{entity}:{id}[:{extraSegment}...]</c>.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant identifier used to namespace the key (e.g., <c>"tenant-a"</c>,
    /// a GUID string). Must not be null or whitespace. This value is always supplied
    /// explicitly — never resolved from ambient context.
    /// </param>
    /// <param name="entity">
    /// The entity type or resource name (e.g., <c>"invoice"</c>, <c>"user-profile"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="id">
    /// The entity identifier (e.g., <c>"42"</c>, a GUID string).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="extraSegments">
    /// Optional additional segments to append after <paramref name="id"/>. Each segment
    /// is separated by <c>:</c>. Common uses: locale (<c>"en-GB"</c>), shard key.
    /// </param>
    /// <returns>
    /// A non-empty string key in the format
    /// <c>{service}:{tenant}:{entity}:{id}[:{extraSegment}...]</c>.
    /// Different <paramref name="tenantId"/> values always produce different keys for the
    /// same <paramref name="entity"/> and <paramref name="id"/> combination.
    /// </returns>
    string BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments);
}
