using SharedKernel.Caching.FusionCache.Diagnostics;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Telemetry;

/// <summary>
/// Pins the telemetry names as literals: dashboards and alerts are built on them, so a rename must fail here first.
/// </summary>
public sealed class CachingTelemetryContractTests
{
    [Fact]
    public void SourceAndMeter_AreSharedKernelCaching() =>
        Assert.Equal(("SharedKernel.Caching", "SharedKernel.Caching"), (CachingTelemetry.SourceName, CachingTelemetry.MeterName));

    [Fact]
    public void InstrumentNames_ArePinned()
    {
        Assert.Equal("sharedkernel.caching.hits", CachingTelemetry.HitsInstrument);
        Assert.Equal("sharedkernel.caching.misses", CachingTelemetry.MissesInstrument);
        Assert.Equal("sharedkernel.caching.errors", CachingTelemetry.ErrorsInstrument);
        Assert.Equal("sharedkernel.caching.evictions", CachingTelemetry.EvictionsInstrument);
        Assert.Equal("sharedkernel.caching.factory.duration", CachingTelemetry.FactoryDurationInstrument);
    }

    [Fact]
    public void TagKeys_ArePinned()
    {
        Assert.Equal("caching.key_prefix", CachingTagKeys.KeyPrefix);
        Assert.Equal("caching.level", CachingTagKeys.Level);
        Assert.Equal("caching.operation", CachingTagKeys.Operation);
        Assert.Equal("caching.eviction_reason", CachingTagKeys.EvictionReason);
        Assert.Equal("caching.result", CachingTagKeys.Result);
    }

    [Fact]
    public void TagValues_ArePinned()
    {
        Assert.Equal("l1", CachingTagValues.LevelMemory);
        Assert.Equal("l2", CachingTagValues.LevelDistributed);
        Assert.Equal("hit", CachingTagValues.ResultHit);
        Assert.Equal("miss", CachingTagValues.ResultMiss);
        Assert.Equal("get", CachingTagValues.OperationGet);
        Assert.Equal("get_or_set", CachingTagValues.OperationGetOrSet);
        Assert.Equal("set", CachingTagValues.OperationSet);
        Assert.Equal("factory", CachingTagValues.OperationFactory);
    }

    [Fact]
    public void NoCachingTagKey_UsesTheRetiredCachePrefix() =>
        Assert.All(
            typeof(CachingTagKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!),
            key => Assert.StartsWith("caching.", key, StringComparison.Ordinal));
}
