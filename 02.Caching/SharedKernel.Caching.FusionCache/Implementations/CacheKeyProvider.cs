using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// Builds global and tenant keys in the <see cref="CacheKeyFormat"/> format, prefixed with
/// <see cref="CachingOptions.ServiceName"/>, the single source of the service name.
/// </summary>
internal sealed class CacheKeyProvider : ITenantCacheKeyProvider
{
    private readonly string _serviceName;

    public CacheKeyProvider(IOptions<CachingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _serviceName = options.Value.ServiceName;
    }

    public string BuildKey(string entity, string id, params string[] segments) =>
        CacheKeyFormat.BuildKey(_serviceName, entity, id, segments);

    public string BuildTenantKey(string tenantId, string entity, string id, params string[] segments) =>
        CacheKeyFormat.BuildTenantKey(_serviceName, tenantId, entity, id, segments);
}
