using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>
/// An <see cref="ITenantCacheKeyProvider"/> double producing exactly the format
/// <see cref="CacheKeyFormat"/> defines, under a fixed service name.
/// </summary>
/// <remarks>
/// Delegates to <see cref="CacheKeyFormat"/> rather than reimplementing the format, so a test
/// asserting on a key is asserting on the real escaping and the real tenant form.
/// </remarks>
internal sealed class FakeCacheKeyProvider : ITenantCacheKeyProvider
{
    internal const string ServiceName = "tests";

    public string BuildKey(string entity, string id, params string[] segments)
        => CacheKeyFormat.BuildKey(ServiceName, entity, id, segments);

    public string BuildTenantKey(string tenantId, string entity, string id, params string[] segments)
        => CacheKeyFormat.BuildTenantKey(ServiceName, tenantId, entity, id, segments);
}
