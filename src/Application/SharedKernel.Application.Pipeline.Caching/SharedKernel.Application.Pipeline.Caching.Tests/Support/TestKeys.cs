using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Pipeline.Caching.Tests.Support;

/// <summary>Builds the key a test expects, through the same public format the behaviors use.</summary>
internal static class TestKeys
{
    internal static string Global(string queryType, string id) =>
        CacheKeyFormat.BuildKey(FakeCacheKeyProvider.ServiceName, queryType, id);

    internal static string Tenant(TenantId tenantId, string queryType, string id) =>
        CacheKeyFormat.BuildTenantKey(FakeCacheKeyProvider.ServiceName, tenantId, queryType, id);

    internal static string User(TenantId? tenantId, string userId, string queryType, string id) =>
        tenantId is { } tenant
            ? CacheKeyFormat.BuildTenantKey(FakeCacheKeyProvider.ServiceName, tenant, queryType, id, "u", userId)
            : CacheKeyFormat.BuildKey(FakeCacheKeyProvider.ServiceName, queryType, id, "u", userId);

    internal static string TenantTag(TenantId tenantId, string tag) =>
        CacheKeyFormat.BuildTenantTag(tenantId, tag);
}
