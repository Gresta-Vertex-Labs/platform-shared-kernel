using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Caching;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Caching.Diagnostics;
using SharedKernel.Application.Pipeline.Caching.Shared;
using SharedKernel.Application.Pipeline.Caching.Tests.Support;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Caching.Tests.Telemetry;

/// <summary>The cache instruments, request-span tags and log events, recorded through the shared helpers.</summary>
public sealed class CachingTelemetryTests
{
    private const string Meter = ApplicationDiagnostics.MeterName;
    private const string Lookups = "sharedkernel.application.cache.lookups";
    private const string Evictions = "sharedkernel.application.cache.evictions";

    private static readonly Guid SentinelTenant = Guid.Parse("5e7a1d00-0000-4000-8000-00000000c0de");

    private static readonly IMeterFactory Meters =
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    private sealed record HitQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "k";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TenantQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "k";
    }

    private sealed record WriteCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<HitQuery>("k")];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["widgets"];
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TenantWriteCommand : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate => [CacheKeyRef.For<TenantQuery>("k")];
    }

    private static CachingBehavior<TRequest, Result<string>> Query<TRequest>(
        FakeCacheService cache,
        ILogger<CachingBehavior<TRequest, Result<string>>> logger,
        TenantId? tenant = null)
        where TRequest : IQueryBase, ICacheableQuery, IRequest<Result<string>>
        => new(
            cache,
            new FakeCacheKeyProvider(),
            new CachingBehaviorsMetrics(Meters),
            logger,
            tenant is null ? null : new FakeRequestContext(tenant));

    private static CacheInvalidationBehavior<TRequest, Result> Command<TRequest>(
        FakeCacheService cache,
        FakeCommandScope scope,
        ILogger<CacheInvalidationBehavior<TRequest, Result>> logger,
        TenantId? tenant = null)
        where TRequest : ICommandBase, IInvalidatesCache, IRequest<Result>
        => new(
            cache,
            new FakeCacheKeyProvider(),
            scope,
            new CachingBehaviorsMetrics(Meters),
            logger,
            tenant is null ? null : new FakeRequestContext(tenant));

    private static Task<Result<string>> Ok() => Task.FromResult(Result<string>.Success("v"));

    private static object? Tag(RecordedMeasurement measurement, string key)
        => measurement.TryGetTag(key, out var value) ? value : null;

    [Theory]
    [InlineData(nameof(CacheOutcome.Hit), "hit")]
    [InlineData(nameof(CacheOutcome.Miss), "miss")]
    [InlineData(nameof(CacheOutcome.Refreshed), "refreshed")]
    [InlineData(nameof(CacheOutcome.NotCachedFailure), "not_cached_failure")]
    [InlineData(nameof(CacheOutcome.NotCachedByPredicate), "not_cached_by_predicate")]
    [InlineData(nameof(CacheOutcome.BypassedScopeUnavailable), "bypassed_scope_unavailable")]
    public void CacheTelemetryNames_MapsEveryOutcomeToItsSnakeName(string outcome, string expected)
        => CacheTelemetryNames.Of(Enum.Parse<CacheOutcome>(outcome)).Should().Be(expected);

    [Fact]
    public void CacheTelemetryNames_CoversEveryDefinedOutcomeAndScope()
    {
        // A new enum member fails here until the switch maps it.
        foreach (var outcome in Enum.GetValues<CacheOutcome>())
            CacheTelemetryNames.Of(outcome).Should().MatchRegex("^[a-z]+(_[a-z]+)*$");

        CacheTelemetryNames.Of(CacheScope.Global).Should().Be("global");
        CacheTelemetryNames.Of(CacheScope.Tenant).Should().Be("tenant");
        CacheTelemetryNames.Of(CacheScope.User).Should().Be("user");
        foreach (var scope in Enum.GetValues<CacheScope>())
            CacheTelemetryNames.Of(scope).Should().MatchRegex("^[a-z]+$");

        CacheTelemetryNames.Target(isTag: false).Should().Be("key");
        CacheTelemetryNames.Target(isTag: true).Should().Be("tag");

        var undefinedOutcome = () => CacheTelemetryNames.Of((CacheOutcome)99);
        undefinedOutcome.Should().Throw<UnreachableException>();
        var undefinedScope = () => CacheTelemetryNames.Of((CacheScope)99);
        undefinedScope.Should().Throw<UnreachableException>();
    }

    [Fact]
    public async Task Lookup_MissThenHit_RecordsTheRenamedCounterWithSnakeValues()
    {
        using var recorder = MetricRecorder.StartRecording(Meter);
        var behavior = Query<HitQuery>(new FakeCacheService(), new InMemoryLogger<CachingBehavior<HitQuery, Result<string>>>());

        await behavior.Handle(new HitQuery(), Ok, CancellationToken.None);
        await behavior.Handle(new HitQuery(), Ok, CancellationToken.None);

        var queryType = CacheEntityName.For(typeof(HitQuery));
        var mine = recorder.MeasurementsOf(Lookups).Where(m => Equals(Tag(m, "application.query_type"), queryType)).ToList();
        mine.Select(m => Tag(m, "application.cache_result")).Should().Equal("miss", "hit");
        mine.Should().OnlyContain(m => Equals(Tag(m, "application.cache_scope"), "global"));
        mine.Should().OnlyContain(m => m.Tags.Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(
            new[] { "application.cache_result", "application.cache_scope", "application.query_type" }));
        recorder.ShouldHaveInstrument(Lookups, "{lookup}", InstrumentKind.Counter);
        recorder.MeasurementsOf("sharedkernel.application.query.cache.outcome").Should().BeEmpty();
    }

    [Fact]
    public async Task Lookup_TheRequestSpanCarriesTheRenamedCacheTags()
    {
        using var source = new ActivitySource("SharedKernel.Application.Tests." + Guid.NewGuid().ToString("N"));
        using var recorder = ActivityRecorder.StartRecording(source.Name);
        var behavior = Query<HitQuery>(new FakeCacheService(), new InMemoryLogger<CachingBehavior<HitQuery, Result<string>>>());

        using (var span = source.StartActivity("request"))
        {
            await behavior.Handle(new HitQuery(), Ok, CancellationToken.None);
        }

        var recorded = recorder.RecordedActivities.Single();
        recorded.ShouldHaveTag("application.cache_result", "miss").ShouldHaveTag("application.cache_scope", "global");
        recorded.ShouldNotHaveTag("sharedkernel.cache.outcome");
    }

    [Fact]
    public async Task Lookup_ScopeUnavailable_LogsWarningAndTheDebugResult()
    {
        var logger = new InMemoryLogger<CachingBehavior<TenantQuery, Result<string>>>();
        var behavior = Query<TenantQuery>(new FakeCacheService(), logger, tenant: null);

        await behavior.Handle(new TenantQuery(), Ok, CancellationToken.None);

        logger.Records.ShouldHaveLogged(new EventId(5201), LogLevel.Warning).Message.Should().Contain("tenant");
        logger.Records.ShouldHaveLogged(new EventId(5200), LogLevel.Debug);
        logger.Records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Application, 2);
    }

    [Fact]
    public async Task Lookup_TenantScoped_NeverPutsTheTenantIdInATagOrALog()
    {
        using var recorder = MetricRecorder.StartRecording(Meter);
        var logger = new InMemoryLogger<CachingBehavior<TenantQuery, Result<string>>>();
        var cache = new FakeCacheService();
        var behavior = Query<TenantQuery>(cache, logger, new TenantId(SentinelTenant));

        await behavior.Handle(new TenantQuery(), Ok, CancellationToken.None);

        cache.Keys.Should().Contain(key => key.Contains(SentinelTenant.ToString(), StringComparison.OrdinalIgnoreCase));
        recorder.MeasurementsOf(Lookups)
            .Where(m => Equals(Tag(m, "application.query_type"), CacheEntityName.For(typeof(TenantQuery))))
            .Should().OnlyContain(m => m.Tags.All(t =>
                t.Value == null || !t.Value.ToString()!.Contains(SentinelTenant.ToString(), StringComparison.OrdinalIgnoreCase)));
        logger.Records.ShouldNotLeak(SentinelTenant.ToString(), SentinelTenant.ToString("N"));
    }

    [Fact]
    public async Task Eviction_Succeeded_RecordsKeyAndTagTargets()
    {
        using var recorder = MetricRecorder.StartRecording(Meter);
        var logger = new InMemoryLogger<CacheInvalidationBehavior<WriteCommand, Result>>();
        var scope = new FakeCommandScope();
        var behavior = Command<WriteCommand>(new FakeCacheService(), scope, logger);

        await behavior.Handle(new WriteCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        await scope.RunCallbacksAsync();

        var commandType = CacheEntityName.For(typeof(WriteCommand));
        var mine = recorder.MeasurementsOf(Evictions).Where(m => Equals(Tag(m, "application.command_type"), commandType)).ToList();
        mine.Select(m => Tag(m, "application.cache_target")).Should().BeEquivalentTo(new object[] { "key", "tag" });
        mine.Should().OnlyContain(m => Equals(Tag(m, "application.cache_evicted"), true));
        mine.Should().OnlyContain(m => m.Tags.Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(
            new[] { "application.cache_evicted", "application.cache_target", "application.command_type" }));
        recorder.ShouldHaveInstrument(Evictions, "{eviction}", InstrumentKind.Counter);
        logger.Records.ShouldHaveLogged(new EventId(5210), LogLevel.Debug);
        logger.Records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Application, 2);
    }

    [Fact]
    public async Task Eviction_Failed_LogsErrorWithTheExceptionAndAddsTheExceptionEventWithoutChangingTheStatus()
    {
        using var source = new ActivitySource("SharedKernel.Application.Tests." + Guid.NewGuid().ToString("N"));
        using var spans = ActivityRecorder.StartRecording(source.Name);
        using var recorder = MetricRecorder.StartRecording(Meter);
        var cache = new FakeCacheService();
        cache.FailEvictionsFor("widgets");
        var logger = new InMemoryLogger<CacheInvalidationBehavior<WriteCommand, Result>>();
        var scope = new FakeCommandScope();
        var behavior = Command<WriteCommand>(cache, scope, logger);

        await behavior.Handle(new WriteCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);
        using (var span = source.StartActivity("callbacks"))
        {
            await scope.RunCallbacksAsync();
        }

        var record = logger.Records.ShouldHaveLogged(new EventId(5211), LogLevel.Error);
        record.Exception.Should().NotBeNull();
        record.Message.Should().Contain("tag").And.NotContain("widgets");
        var failed = recorder.MeasurementsOf(Evictions)
            .Where(m => Equals(Tag(m, "application.command_type"), CacheEntityName.For(typeof(WriteCommand)))
                && Equals(Tag(m, "application.cache_target"), "tag"))
            .ToList();
        failed.Should().ContainSingle();
        Tag(failed[0], "application.cache_evicted").Should().Be(false);
        var callbacks = spans.RecordedActivities.Single();
        callbacks.ShouldHaveEvent("exception");
        callbacks.Status.Should().Be(ActivityStatusCode.Unset);
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Eviction_ScopeUnavailable_LogsWarning()
    {
        var logger = new InMemoryLogger<CacheInvalidationBehavior<TenantWriteCommand, Result>>();
        var scope = new FakeCommandScope();
        var behavior = Command<TenantWriteCommand>(new FakeCacheService(), scope, logger, tenant: null);

        await behavior.Handle(new TenantWriteCommand(), () => Task.FromResult(Result.Success()), CancellationToken.None);

        logger.Records.ShouldHaveLogged(new EventId(5212), LogLevel.Warning);
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public void EventIds_AreInsideSubBlockTwoWithLiteralPins()
    {
        PipelineCachingLoggingEventIds.QueryCacheResult.Should().Be(5200);
        PipelineCachingLoggingEventIds.QueryScopeUnavailable.Should().Be(5201);
        PipelineCachingLoggingEventIds.InvalidationCompleted.Should().Be(5210);
        PipelineCachingLoggingEventIds.InvalidationEntryFailed.Should().Be(5211);
        PipelineCachingLoggingEventIds.InvalidationScopeUnavailable.Should().Be(5212);
    }

    [Fact]
    public void Behaviors_LogUnderTheirOwnCategoryNotTheQueryType()
    {
        var factory = new InMemoryLoggerFactory();
        _ = Query<HitQuery>(new FakeCacheService(), factory.CreateLogger<CachingBehavior<HitQuery, Result<string>>>());

        factory.ShouldHaveCategory(typeof(CachingBehavior<HitQuery, Result<string>>));
        factory.Loggers.Keys.Should().NotContain(key => key.EndsWith(nameof(HitQuery), StringComparison.Ordinal));
    }
}
