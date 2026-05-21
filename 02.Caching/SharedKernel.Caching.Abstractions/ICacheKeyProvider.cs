namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Constructs cache keys using the platform-standard key format.
/// </summary>
/// <remarks>
/// <para>
/// The key format contract is: <c>{service}:{entity}:{id}[:{extraSegment}...][:{version}]</c>
/// where <c>{service}</c> is the owning service name (e.g., <c>"order-svc"</c>),
/// <c>{entity}</c> is the entity type (e.g., <c>"invoice"</c>), and <c>{id}</c> is
/// the entity identifier. Additional segments are appended with <c>:</c> separator.
/// When a non-zero <c>version</c> is supplied via <see cref="BuildKey(string,string,int,string[])"/>,
/// a <c>:v{version}</c> suffix is appended after all other segments.
/// </para>
/// <para>
/// Example outputs:
/// <list type="bullet">
///   <item><description><c>order-svc:invoice:42</c> (no version)</description></item>
///   <item><description><c>order-svc:invoice:42:v3</c> (version 3)</description></item>
///   <item><description><c>order-svc:invoice:42:en-GB:v3</c> (extra segment + version 3)</description></item>
/// </list>
/// </para>
/// <para>
/// Callers must use this interface rather than constructing key strings inline.
/// The default implementation is registered by <c>AddSharedKernelCaching</c> in
/// <c>SharedKernel.Caching.FusionCache</c> and reads the service name from
/// <c>CachingOptions.ServiceName</c>.
/// </para>
/// </remarks>
public interface ICacheKeyProvider
{
    /// <summary>
    /// Builds a canonical cache key in the format
    /// <c>{service}:{entity}:{id}[:{extraSegment}...]</c>.
    /// </summary>
    /// <param name="entity">
    /// The entity type or resource name (e.g., <c>"invoice"</c>, <c>"user-profile"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="id">
    /// The entity identifier (e.g., <c>"42"</c>, a GUID string).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="extraSegments">
    /// Optional additional segments to append. Each segment is separated by <c>:</c>.
    /// Common uses: locale (<c>"en-GB"</c>), tenant scope.
    /// </param>
    /// <returns>
    /// A non-empty string key in the format
    /// <c>{service}:{entity}:{id}[:{extraSegment}...]</c>.
    /// </returns>
    /// <remarks>
    /// Callers who need to pass a <see cref="CachePolicy.KeyVersion"/> value should use
    /// <see cref="BuildKey(string, string, int, string[])"/> instead to append the
    /// <c>:v{version}</c> suffix automatically.
    /// </remarks>
    string BuildKey(string entity, string id, params string[] extraSegments);

    /// <summary>
    /// Builds a canonical cache key with an explicit schema version suffix in the format
    /// <c>{service}:{entity}:{id}[:{extraSegment}...]:v{version}</c>.
    /// When <paramref name="version"/> is <c>0</c>, the output is identical to
    /// <see cref="BuildKey(string, string, string[])"/> — no suffix is appended.
    /// </summary>
    /// <param name="entity">
    /// The entity type or resource name (e.g., <c>"invoice"</c>, <c>"user-profile"</c>).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="id">
    /// The entity identifier (e.g., <c>"42"</c>, a GUID string).
    /// Must not be null or whitespace.
    /// </param>
    /// <param name="version">
    /// The schema version from <see cref="CachePolicy.KeyVersion"/>. When greater than zero,
    /// a <c>:v{version}</c> suffix is appended after all other segments. Version <c>0</c>
    /// produces no suffix — identical key format to today (backward-compatible).
    /// </param>
    /// <param name="extraSegments">
    /// Optional additional segments to append before the version suffix. Each segment is
    /// separated by <c>:</c>.
    /// </param>
    /// <returns>
    /// A non-empty string key. When <paramref name="version"/> &gt; 0 the key ends with
    /// <c>:v{version}</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Deployment workflow:</strong> increment <see cref="CachePolicy.KeyVersion"/>
    /// → deploy → old keys expire naturally via their TTL — no explicit cache flush is required.
    /// Both the old and new keys coexist in the cache during the transition period; old keys
    /// simply age out.
    /// </para>
    /// <para>
    /// Key versioning is the <strong>caller's responsibility</strong>. Pass
    /// <c>CachePolicy.KeyVersion</c> to this overload to bake the version into the key string;
    /// <c>ICacheService</c> method signatures do not change.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var policy = CachePolicy.Default.WithVersion(3);
    /// var key = keyProvider.BuildKey("invoice", "42", policy.KeyVersion);
    /// // key == "order-svc:invoice:42:v3"
    /// </code>
    /// </para>
    /// </remarks>
    string BuildKey(string entity, string id, int version, params string[] extraSegments);
}
