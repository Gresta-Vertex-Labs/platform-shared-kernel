using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Communication.Internal.Extensions;
using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Internal.Tests;

/// <summary>
/// Regression tests for the P-255 (WO-041) <c>[LoggerMessage]</c> retrofit of
/// <see cref="KubernetesServiceEndpointResolver"/> and <see cref="StaticServiceDiscoveryStartupWarning"/> (T-27).
/// Confirms all nine log call sites (EventId 11300-11308) still carry the documented
/// <c>EventId</c>/<see cref="LogLevel"/> pair after conversion from hand-written
/// <c>LoggerMessage.Define&lt;&gt;</c> delegates (and two ad-hoc <c>logger.LogDebug(...)</c> calls)
/// to <c>[LoggerMessage]</c>-attributed static partial methods, with no behavioral change.
/// </summary>
public sealed class LoggingEventIdRegressionTests
{
    // ─────────────────────────────────────────────────────────────
    // Static metadata verification — every [LoggerMessage] attribute on both types
    // ─────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> ExpectedKubernetesResolverLogMethods()
    {
        yield return ["LogSrvLookupAttempt", 11300, LogLevel.Debug];
        yield return ["LogARecordLookupAttempt", 11301, LogLevel.Debug];
        yield return ["LogDnsFallback", 11302, LogLevel.Warning];
        yield return ["LogServiceResolved", 11303, LogLevel.Debug];
        yield return ["LogStaleCacheUsed", 11304, LogLevel.Warning];
        yield return ["LogCacheHit", 11305, LogLevel.Debug];
        yield return ["LogSrvLookupFailed", 11306, LogLevel.Debug];
        yield return ["LogARecordLookupFailed", 11307, LogLevel.Debug];
    }

    [Theory]
    [MemberData(nameof(ExpectedKubernetesResolverLogMethods))]
    public void KubernetesServiceEndpointResolver_LogMethod_CarriesExpectedEventIdAndLevel(
        string methodName, int expectedEventId, LogLevel expectedLevel)
    {
        var method = typeof(KubernetesServiceEndpointResolver).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull($"the retrofitted [LoggerMessage] method '{methodName}' must still exist");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(expectedEventId);
        attribute.Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void StaticServiceDiscoveryStartupWarning_LogStaticServiceDiscoveryActive_CarriesExpectedEventIdAndLevel()
    {
        var method = typeof(StaticServiceDiscoveryStartupWarning).GetMethod(
            "LogStaticServiceDiscoveryActive", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("the retrofitted [LoggerMessage] method must still exist");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(11308);
        attribute.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void AllNineLogEventIds_AreDistinct_AndFallWithinTheInternalSubBlock()
    {
        var kubernetesEventIds = ExpectedKubernetesResolverLogMethods()
            .Select(row => (int)row[1])
            .ToList();
        var allEventIds = kubernetesEventIds.Append(11308).ToList();

        allEventIds.Should().HaveCount(9);
        allEventIds.Should().OnlyHaveUniqueItems();
        allEventIds.Should().OnlyContain(id => id >= 11300 && id <= 11399,
            "the Internal package's reserved sub-block is 11300-11399");
    }

    // ─────────────────────────────────────────────────────────────
    // Runtime verification — the call sites reachable via the pass-through
    // ServiceDiscovery provider actually fire at the documented EventId/Level.
    // SRV/A-record-failure, DNS-fallback, and stale-cache paths require a
    // fault-injectable DNS provider and remain untestable in unit scope
    // (same documented limitation as TtlCacheTests' Integration-tagged tests).
    // ─────────────────────────────────────────────────────────────

    private static (ServiceProvider Provider, TestLogSink Sink) BuildProviderWithSink(int ttlSeconds)
    {
        var sink = new TestLogSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new TestLoggerProvider(sink));
        });
        services.AddK8sServiceDiscovery(opts => opts.EndpointCacheTtlSeconds = ttlSeconds);
        services.AddPassThroughServiceEndpointProvider();
        return (services.BuildServiceProvider(), sink);
    }

    [Fact]
    public async Task ResolveAsync_FirstCall_LogsSrvLookupAttemptAndServiceResolved()
    {
        var (sp, sink) = BuildProviderWithSink(ttlSeconds: 60);
        await using var _ = sp;
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        await resolver.ResolveAsync("regression-test-service", CancellationToken.None);

        sink.Entries.Should().Contain(e => e.EventId.Id == 11300 && e.LogLevel == LogLevel.Debug);
        sink.Entries.Should().Contain(e => e.EventId.Id == 11303 && e.LogLevel == LogLevel.Debug);
    }

    [Fact]
    public async Task ResolveAsync_SecondCallWithinTtl_LogsCacheHit()
    {
        var (sp, sink) = BuildProviderWithSink(ttlSeconds: 60);
        await using var _ = sp;
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        await resolver.ResolveAsync("cache-hit-regression-service", CancellationToken.None);
        sink.Entries.Clear();
        await resolver.ResolveAsync("cache-hit-regression-service", CancellationToken.None);

        sink.Entries.Should().Contain(e => e.EventId.Id == 11305 && e.LogLevel == LogLevel.Debug);
    }
}
