namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Constructs cache keys using the platform-standard key format.
/// </summary>
/// <remarks>
/// <para>
/// The key format contract is: <c>{service}:{entity}:{id}[:{extraSegment}...]</c>
/// where <c>{service}</c> is the owning service name (e.g., <c>"order-svc"</c>),
/// <c>{entity}</c> is the entity type (e.g., <c>"invoice"</c>), and <c>{id}</c> is
/// the entity identifier. Additional segments are appended with <c>:</c> separator.
/// </para>
/// <para>
/// Example outputs:
/// <list type="bullet">
///   <item><description><c>order-svc:invoice:42</c></description></item>
///   <item><description><c>order-svc:invoice:42:v2</c></description></item>
/// </list>
/// </para>
/// <para>
/// Callers must use this interface rather than constructing key strings inline.
/// The default implementation is registered by <c>AddSharedKernelCaching</c> in
/// <c>SharedKernel.Caching</c> and reads the service name from <c>CachingOptions.ServiceName</c>.
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
    /// Common uses: version tags (<c>"v2"</c>), locale (<c>"en-GB"</c>), tenant scope.
    /// </param>
    /// <returns>
    /// A non-empty string key in the format
    /// <c>{service}:{entity}:{id}[:{extraSegment}...]</c>.
    /// </returns>
    string BuildKey(string entity, string id, params string[] extraSegments);
}
