using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Support;

/// <summary>Builds the key a test expects, through the same public format the behaviors use.</summary>
internal static class TestKeys
{
    internal static string Global(string queryType, string id) =>
        CacheKeyFormat.BuildKey(FakeCacheKeyProvider.ServiceName, queryType, id);

    internal static string Tenant(Guid tenantId, string queryType, string id) =>
        CacheKeyFormat.BuildTenantKey(FakeCacheKeyProvider.ServiceName, tenantId.ToString("D"), queryType, id);

    internal static string User(Guid? tenantId, string userId, string queryType, string id) =>
        tenantId is { } tenant
            ? CacheKeyFormat.BuildTenantKey(FakeCacheKeyProvider.ServiceName, tenant.ToString("D"), queryType, id, "u", userId)
            : CacheKeyFormat.BuildKey(FakeCacheKeyProvider.ServiceName, queryType, id, "u", userId);

    internal static string TenantTag(Guid tenantId, string tag) =>
        CacheKeyFormat.BuildTenantTag(tenantId.ToString("D"), tag);
}
