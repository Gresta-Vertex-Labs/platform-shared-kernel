using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using SharedKernel.Execution.Tenancy;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

/// <summary>
/// Metrics, spans and logs carry only <c>{service}:{entity}</c> key prefixes and <c>@tenant</c>
/// placeholders: never an entity id and never a tenant id.
/// </summary>
public sealed class TelemetryRedactionTests
{
    private const string TenantText = "5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6";
    private static readonly TenantId Tenant = TenantId.Parse(TenantText);
    private const string EntityId = "id-secret-42";

    // -------------------------------------------------------------------------
    // ExtractKeyPrefix
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("svc:entity:123", "svc:entity")]
    [InlineData("svc:entity:123:extra", "svc:entity")]
    [InlineData("svc:entity:123:extra:more", "svc:entity")]
    [InlineData("svc:entity", "svc:entity")]
    [InlineData("svc", "svc")]
    [InlineData("", "")]
    public void ExtractKeyPrefix_GlobalAndShortKeys(string key, string expected) =>
        Assert.Equal(expected, FusionCacheService.ExtractKeyPrefix(key));

    [Theory]
    [InlineData("svc:@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6:orders:id-secret-42", "svc:orders")]
    [InlineData("svc:@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6:orders:id-secret-42:lines:3", "svc:orders")]
    [InlineData("svc:@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6:orders", "svc:orders")]
    [InlineData("svc:@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6", "svc")]
    public void ExtractKeyPrefix_TenantKeys_DropTheTenantAndTheId(string key, string expected)
    {
        var prefix = FusionCacheService.ExtractKeyPrefix(key);

        Assert.Equal(expected, prefix);
        Assert.DoesNotContain(TenantText, prefix, StringComparison.Ordinal);
        Assert.DoesNotContain(EntityId, prefix, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractKeyPrefix_KeysBuiltByCacheKeyFormat_NeverContainTheTenantOrTheId()
    {
        // Escaped parts (':' and '@' inside an id) must not shift the segments.
        const string trickyId = "a:b@c";

        var tenantKey = CacheKeyFormat.BuildTenantKey("orders-api", Tenant, "invoice", trickyId, "v2");
        var globalKey = CacheKeyFormat.BuildKey("orders-api", "invoice", trickyId, "v2");

        Assert.Equal("orders-api:invoice", FusionCacheService.ExtractKeyPrefix(tenantKey));
        Assert.Equal("orders-api:invoice", FusionCacheService.ExtractKeyPrefix(globalKey));
    }

    // -------------------------------------------------------------------------
    // DescribeTag
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6:orders", "@tenant:orders")]
    [InlineData("@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6:orders:open", "@tenant:orders:open")]
    [InlineData("@5d8e1f2a-3b4c-4d5e-8f60-718293a4b5c6", "@tenant")]
    [InlineData("orders", "orders")]
    [InlineData("orders:open", "orders:open")]
    [InlineData("", "")]
    public void DescribeTag_ReplacesTheTenantId(string tag, string expected) =>
        Assert.Equal(expected, FusionCacheService.DescribeTag(tag));

    [Fact]
    public void DescribeTag_TagsBuiltByCacheKeyFormat_NeverContainTheTenant()
    {
        Assert.Equal("@tenant:orders", FusionCacheService.DescribeTag(CacheKeyFormat.BuildTenantTag(Tenant, "orders")));
        Assert.Equal("@tenant", FusionCacheService.DescribeTag(CacheKeyFormat.BuildTenantWideTag(Tenant)));
        Assert.Equal("@tenant:a%3Ab", FusionCacheService.DescribeTag(CacheKeyFormat.BuildTenantTag(Tenant, "a:b")));
    }

    // -------------------------------------------------------------------------
    // End to end: every call path through FusionCacheService
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EveryOperation_OnTenantKeys_EmitsNoTenantOrIdInLogsMetricsOrSpans()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddTenantCacheService();
        await using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ICacheService>();
        var tenantCache = provider.GetRequiredService<ITenantCacheService>();
        var key = CacheKeyFormat.BuildTenantKey("svc", Tenant, "orders", EntityId);
        var otherKey = CacheKeyFormat.BuildTenantKey("svc", Tenant, "orders", EntityId + "-b");
        var tagged = CachePolicy.Default.WithTags("open").ForTenant(Tenant);

        using var metrics = new MetricRecorder();
        using var spans = new ActivityRecorder();

        await cache.TryGetAsync<string>(key);                                          // miss
        await cache.SetAsync(key, "v", tagged);                                         // set
        await cache.TryGetAsync<string>(key);                                           // L1 hit
        await cache.GetOrSetAsync(otherKey, _ => ValueTask.FromResult("f"), tagged);    // factory run
        await cache.TryGetManyAsync<string>([key, otherKey + "-missing"]);              // hit + miss
        await cache.SetManyAsync(new Dictionary<string, string> { [otherKey] = "m" }, tagged);
        await cache.ExpireAsync(key);
        await cache.RemoveAsync(otherKey);
        await cache.RemoveByTagAsync(CacheKeyFormat.BuildTenantTag(Tenant, "open"));
        await cache.RemoveByTagsAsync([CacheKeyFormat.BuildTenantWideTag(Tenant)]);
        await tenantCache.RemoveTenantAsync(Tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await cache.GetOrSetAsync<string>(key + ":boom", _ => throw new InvalidOperationException($"{TenantText} {EntityId}"), tagged));

        Assert.True(await Eventually.HoldsAsync(() => metrics.Sum("cache.hits", "svc:orders", "l1") >= 2));

        // Our own log category: present, and redacted.
        var ownLogs = logs.Logs.Where(l => l.Category == typeof(FusionCacheService).FullName).ToList();
        Assert.Contains(ownLogs, l => l.Message.Contains("svc:orders", StringComparison.Ordinal));
        Assert.Contains(ownLogs, l => l.Message.Contains("@tenant:open", StringComparison.Ordinal));
        Assert.Contains(ownLogs, l => l.Message == "Cache entries removed for tag @tenant");
        foreach (var log in ownLogs)
        {
            AssertRedacted(log.Message);
            foreach (var (_, value) in log.State)
                AssertRedacted(value?.ToString());
        }

        Assert.NotEmpty(metrics.Measurements);
        foreach (var measurement in metrics.Measurements)
        {
            foreach (var (_, value) in measurement.Tags)
                AssertRedacted(value?.ToString());
        }

        Assert.NotEmpty(spans.Activities);
        foreach (Activity span in spans.Activities)
        {
            AssertRedacted(span.DisplayName);
            AssertRedacted(span.StatusDescription);
            foreach (var (_, value) in span.TagObjects)
                AssertRedacted(value?.ToString());
        }
    }

    [Fact]
    public async Task FactoryFailure_SpanStatusDescription_IsTheExceptionTypeName_NotTheMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ICacheService>();
        var key = CacheKeyFormat.BuildKey("svc", "status-" + Guid.NewGuid().ToString("N"), EntityId);

        using var spans = new ActivityRecorder();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await cache.GetOrSetAsync<string>(key, _ => throw new InvalidOperationException("secret message"), CachePolicy.Default));

        var span = Assert.Single(spans.Activities, a =>
            a.OperationName == "cache.get_or_set" && (string?)a.GetTagItem("cache.key_prefix") == FusionCacheService.ExtractKeyPrefix(key));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(nameof(InvalidOperationException), span.StatusDescription);
    }

    private static void AssertRedacted(string? text)
    {
        if (text is null)
            return;

        Assert.DoesNotContain(TenantText, text, StringComparison.Ordinal);
        Assert.DoesNotContain(EntityId, text, StringComparison.Ordinal);
    }
}
