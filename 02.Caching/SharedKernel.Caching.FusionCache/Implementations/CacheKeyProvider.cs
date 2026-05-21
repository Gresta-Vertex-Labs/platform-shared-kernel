using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// Default <see cref="ICacheKeyProvider"/> implementation.
/// Builds cache keys using the platform-standard format:
/// <c>{service}:{entity}:{id}[:{extraSegment}...][:{version}]</c>
/// where <c>{service}</c> is resolved from <see cref="CachingOptions.ServiceName"/>.
/// </summary>
/// <remarks>
/// <para>
/// No casing normalisation is applied — the caller controls the casing of all segments.
/// </para>
/// <para>
/// When a non-zero <c>version</c> is supplied via
/// <see cref="BuildKey(string, string, int, string[])"/>, a <c>:v{version}</c> suffix
/// is appended after all other segments (e.g. <c>order-svc:invoice:42:v3</c>).
/// </para>
/// <para>
/// <strong>Deployment workflow for key versioning:</strong> increment
/// <c>CachePolicy.KeyVersion</c> in code → deploy → old keys expire naturally via
/// their TTL — no explicit cache flush is required. Both old and new keys coexist in the
/// cache during the transition period; old keys simply age out.
/// </para>
/// <para>
/// Register a custom <c>ICacheKeyProvider</c> singleton after <c>AddSharedKernelCaching</c>
/// to override this default for the entire service.
/// </para>
/// </remarks>
internal sealed class CacheKeyProvider : ICacheKeyProvider
{
    private readonly string _serviceName;

    /// <summary>
    /// Initialises a new instance of <see cref="CacheKeyProvider"/>.
    /// </summary>
    /// <param name="options">Validated caching options supplying <see cref="CachingOptions.ServiceName"/>.</param>
    public CacheKeyProvider(IOptions<CachingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _serviceName = options.Value.ServiceName;
    }

    /// <inheritdoc />
    public string BuildKey(string entity, string id, params string[] extraSegments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (extraSegments.Length == 0)
            return $"{_serviceName}:{entity}:{id}";

        // Pre-allocate a string array to avoid intermediate string allocations.
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

        // Compose: {service}:{entity}:{id}[:{extra}...]:v{version}
        var versionSegment = $"v{version}";

        var segmentCount = 3 + extraSegments.Length + 1; // +1 for versionSegment
        var segments = new string[segmentCount];
        segments[0] = _serviceName;
        segments[1] = entity;
        segments[2] = id;
        extraSegments.CopyTo(segments, 3);
        segments[segmentCount - 1] = versionSegment;

        return string.Join(':', segments);
    }
}
