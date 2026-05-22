using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// Default <see cref="ITenantCacheKeyProvider"/> implementation.
/// Builds multi-tenant cache keys using the platform-standard format:
/// <c>{service}:{tenant}:{entity}:{id}[:{extraSegment}...]</c>
/// where <c>{service}</c> is resolved from <see cref="CachingCoreOptions.ServiceName"/>.
/// </summary>
/// <remarks>
/// <para>
/// No casing normalisation is applied — the caller controls the casing of all segments.
/// </para>
/// <para>
/// This class also fully implements <see cref="ICacheKeyProvider"/> (via the
/// <see cref="ITenantCacheKeyProvider"/> inheritance chain) so it can serve as a drop-in
/// provider for both tenant-aware and non-tenant-aware key construction. The non-tenant
/// overloads (<see cref="BuildKey(string, string, string[])"/> and
/// <see cref="BuildKey(string, string, int, string[])"/>) produce keys in the standard
/// <c>{service}:{entity}:{id}</c> format, identical to <c>CacheKeyProvider</c>.
/// </para>
/// <para>
/// Register via <c>AddTenantCacheKeyProvider()</c> on the <see cref="ICachingBuilder"/>.
/// This registration is additive — it does not replace the existing
/// <see cref="ICacheKeyProvider"/> singleton.
/// </para>
/// </remarks>
internal sealed class TenantCacheKeyProvider : ITenantCacheKeyProvider
{
    private readonly string _serviceName;

    /// <summary>
    /// Initialises a new instance of <see cref="TenantCacheKeyProvider"/>.
    /// </summary>
    /// <param name="options">
    /// Validated core caching options supplying <see cref="CachingCoreOptions.ServiceName"/>.
    /// </param>
    public TenantCacheKeyProvider(IOptions<CachingCoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _serviceName = options.Value.ServiceName;
    }

    /// <inheritdoc />
    public string BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (extraSegments.Length == 0)
            return $"{_serviceName}:{tenantId}:{entity}:{id}";

        // Pre-allocate a string array to avoid intermediate string allocations.
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

        // Version 0 is backward-compatible — identical output to the parameterless overload.
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
