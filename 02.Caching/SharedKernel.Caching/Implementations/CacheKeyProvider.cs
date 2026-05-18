using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;

namespace SharedKernel.Caching.Implementations;

/// <summary>
/// Default <see cref="ICacheKeyProvider"/> implementation.
/// Builds cache keys using the platform-standard format:
/// <c>{service}:{entity}:{id}[:{extraSegment}...]</c>
/// where <c>{service}</c> is resolved from <see cref="CachingOptions.ServiceName"/>.
/// </summary>
/// <remarks>
/// No casing normalisation is applied — the caller controls the casing of all segments.
/// Register a custom <c>ICacheKeyProvider</c> singleton after <c>AddSharedKernelCaching</c>
/// to override this default for the entire service.
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
}
