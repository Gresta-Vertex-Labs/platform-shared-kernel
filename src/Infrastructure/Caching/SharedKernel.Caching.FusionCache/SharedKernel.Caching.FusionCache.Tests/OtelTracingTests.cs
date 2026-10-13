using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Diagnostics;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// The cache's spans, recorded through the shared <see cref="ActivityRecorder"/>. The source is static and shared, so
/// every test uses its own entity and looks only at the spans tagged with that entity's key prefix (the assembly also
/// runs serially, see <c>AssemblyInfo</c>).
/// </summary>
public sealed class OtelTracingTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;
    private readonly ActivityRecorder _spans = ActivityRecorder.StartRecording(CachingTelemetry.SourceName);
    private readonly string _entity = "e" + Guid.NewGuid().ToString("N");

    public OtelTracingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose()
    {
        _spans.Dispose();
        _provider.Dispose();
    }

    private string Prefix => "svc:" + _entity;

    private string Key(string id) => CacheKeyFormat.BuildKey("svc", _entity, id);

    private IEnumerable<Activity> Mine() =>
        _spans.RecordedActivities.Where(a => (string?)a.GetTagItem(CachingTagKeys.KeyPrefix) == Prefix);

    [Fact]
    public async Task TryGetAsync_Hit_ProducesAClientSpanWithTheHitResult()
    {
        await _cache.SetAsync(Key("hit"), "value", CachePolicy.Default);
        _spans.Clear();

        var result = await _cache.TryGetAsync<string>(Key("hit"));

        Assert.Equal("value", result.Value);
        Mine().ShouldHaveSingleSpan("caching get")
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveSource(CachingTelemetry.SourceName)
            .ShouldHaveTag(CachingTagKeys.Result, CachingTagValues.ResultHit)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task TryGetAsync_Miss_ProducesAClientSpanWithTheMissResult()
    {
        var result = await _cache.TryGetAsync<string>(Key("miss"));

        Assert.False(result.IsHit);
        Mine().ShouldHaveSingleSpan("caching get")
            .ShouldHaveTag(CachingTagKeys.Result, CachingTagValues.ResultMiss);
    }

    [Fact]
    public async Task SetAsync_ProducesAClientSpanWithTheKeyPrefixOnly()
    {
        await _cache.SetAsync(Key("set"), "value", CachePolicy.Default);

        Mine().ShouldHaveSingleSpan("caching set")
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldNotHaveTag(CachingTagKeys.Result)
            .ShouldBeRecordedSuccess();
    }

    [Fact]
    public async Task GetOrSetAsync_Hit_RunsNoFactory_AndTagsTheHitResult()
    {
        await _cache.SetAsync(Key("cached"), "cached-value", CachePolicy.Default);
        _spans.Clear();

        var factoryInvoked = false;
        var result = await _cache.GetOrSetAsync(
            Key("cached"),
            _ =>
            {
                factoryInvoked = true;
                return ValueTask.FromResult("factory-value");
            },
            CachePolicy.Default);

        Assert.Equal("cached-value", result);
        Assert.False(factoryInvoked);
        Mine().ShouldHaveSingleSpan("caching get_or_set")
            .ShouldHaveTag(CachingTagKeys.Result, CachingTagValues.ResultHit);
    }

    [Fact]
    public async Task GetOrSetAsync_Miss_TagsTheMissResult()
    {
        var result = await _cache.GetOrSetAsync(Key("computed"), _ => ValueTask.FromResult("factory-value"), CachePolicy.Default);

        Assert.Equal("factory-value", result);
        Mine().ShouldHaveSingleSpan("caching get_or_set")
            .ShouldHaveTag(CachingTagKeys.Result, CachingTagValues.ResultMiss);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryException_RecordsTheExceptionOnTheSpan_WithoutTheMessageInTheStatus()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _cache.GetOrSetAsync<string>(Key("boom"), _ => throw new InvalidOperationException("secret factory message"), CachePolicy.Default));

        var span = Mine().ShouldHaveSingleSpan("caching get_or_set")
            .ShouldBeRecordedException(typeof(InvalidOperationException));
        Assert.DoesNotContain("secret factory message", span.StatusDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryCancellation_LeavesTheStatusUnset()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _cache.GetOrSetAsync<string>(
                Key("cancelled"),
                async ct =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return "never";
                },
                CachePolicy.Default,
                cts.Token));

        Mine().ShouldHaveSingleSpan("caching get_or_set").ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task EverySpan_FollowsTheSpanConvention_AndCarriesNoId()
    {
        await _cache.SetAsync(Key("secret-id-42"), "v", CachePolicy.Default);
        await _cache.TryGetAsync<string>(Key("secret-id-42"));
        await _cache.GetOrSetAsync(Key("secret-id-43"), _ => ValueTask.FromResult("v"), CachePolicy.Default);

        var spans = Mine().ToList();
        Assert.Equal(3, spans.Count);
        spans.ShouldFollowSpanConvention().ShouldNotLeak("secret-id-42", "secret-id-43");
    }

    [Fact]
    public async Task TheActivitySource_IsNamedAndVersionedWithTheAssembly()
    {
        await _cache.SetAsync(Key("version"), "v", CachePolicy.Default);

        var source = Mine().Single().Source;

        Assert.Equal("SharedKernel.Caching", source.Name);
        Assert.Equal(TelemetryVersion.Of(typeof(CachingOptions).Assembly), source.Version);
    }
}
