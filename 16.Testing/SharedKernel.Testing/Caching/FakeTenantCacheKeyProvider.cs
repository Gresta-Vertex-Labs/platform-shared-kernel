using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ITenantCacheKeyProvider"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Key format mirrors the production implementation:
/// <list type="bullet">
///   <item>
///     <description>
///     Tenant key: <c>{service}:{tenant}:{entity}:{id}[:{extraSegment}...]</c>
///     </description>
///   </item>
///   <item>
///     <description>
///     Standard key: <c>{service}:{entity}:{id}[:{extraSegment}...]</c>
///     </description>
///   </item>
///   <item>
///     <description>
///     Versioned key: <c>{service}:{entity}:{id}[:{extraSegment}...]:v{version}</c>
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// By default the service name is <c>"test-svc"</c>. Provide a different value via the
/// constructor to match the service name used in the system under test.
/// </para>
/// <para>
/// This fake has zero dependency on <c>12.Security</c>, <c>IHttpContextAccessor</c>,
/// or any infrastructure package — safe to use in pure unit tests.
/// </para>
/// </remarks>
public sealed class FakeTenantCacheKeyProvider : ITenantCacheKeyProvider
{
    private readonly string _serviceName;

    /// <summary>
    /// Initialises a new instance of <see cref="FakeTenantCacheKeyProvider"/> with the
    /// default service name <c>"test-svc"</c>.
    /// </summary>
    public FakeTenantCacheKeyProvider() : this("test-svc")
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="FakeTenantCacheKeyProvider"/> with a
    /// custom service name.
    /// </summary>
    /// <param name="serviceName">
    /// The service name to use as the first key segment. Must not be null or whitespace.
    /// </param>
    public FakeTenantCacheKeyProvider(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        _serviceName = serviceName;
    }

    /// <inheritdoc />
    public string BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (extraSegments.Length == 0)
            return $"{_serviceName}:{tenantId}:{entity}:{id}";

        var segments = new string[4 + extraSegments.Length];
        segments[0] = _serviceName;
        segments[1] = tenantId;
        segments[2] = entity;
        segments[3] = id;
        extraSegments.CopyTo(segments, 4);

        return string.Join(':', segments);
    }

    /// <inheritdoc />
    public string BuildKey(string entity, string id, params string[] extraSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (extraSegments.Length == 0)
            return $"{_serviceName}:{entity}:{id}";

        var segments = new string[3 + extraSegments.Length];
        segments[0] = _serviceName;
        segments[1] = entity;
        segments[2] = id;
        extraSegments.CopyTo(segments, 3);

        return string.Join(':', segments);
    }

    /// <inheritdoc />
    public string BuildKey(string entity, string id, int version, params string[] extraSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(version, nameof(version));

        if (version == 0)
            return BuildKey(entity, id, extraSegments);

        var versionSegment = $"v{version}";

        var segmentCount = 3 + extraSegments.Length + 1;
        var segments = new string[segmentCount];
        segments[0] = _serviceName;
        segments[1] = entity;
        segments[2] = id;
        extraSegments.CopyTo(segments, 3);
        segments[segmentCount - 1] = versionSegment;

        return string.Join(':', segments);
    }
}
