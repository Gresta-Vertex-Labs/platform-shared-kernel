using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Telemetry;
using SharedKernel.Search.Meilisearch.Diagnostics;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Search.Meilisearch.Tests.Index;

/// <summary>
/// Spans and instruments of the Meilisearch provider, asserted through the shared recorders against stubs beneath the
/// SDK's own message handler (no container): the span name and kind, the three <c>search.*</c> tags and nothing else, the
/// failure, exception and cancellation shapes, the instrument names, units and tag sets, the outcome vocabulary, and that
/// no query text, filter value or document id ever becomes a tag.
/// </summary>
public sealed class MeilisearchTelemetryTests
{
    private const string Duration = SearchInstrumentNames.OperationDuration;
    private const string Documents = SearchInstrumentNames.Documents;
    private const string EnqueuedTask =
        "{\"taskUid\":1,\"indexUid\":\"x\",\"status\":\"enqueued\",\"type\":\"documentAdditionOrUpdate\",\"enqueuedAt\":\"2026-01-01T00:00:00Z\"}";

    private static readonly string[] SpanTags = [SearchTagKeys.Index, SearchTagKeys.Operation, SearchTagKeys.Provider];

    private static int _indexSequence;

    private static string NewIndexName() => "telemetry-" + Interlocked.Increment(ref _indexSequence);

    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out object? value) ? value as string : null;

    private static IEnumerable<RecordedMeasurement> DurationsOf(MetricRecorder metrics, string index, string operation) =>
        metrics.MeasurementsOf(Duration).Where(m => Tag(m, SearchTagKeys.Index) == index && Tag(m, SearchTagKeys.Operation) == operation);

    private static List<Activity> SpansOf(ActivityRecorder spans, string index, string operation) =>
        [.. spans.RecordedActivities.Where(a => (string?)a.GetTagItem(SearchTagKeys.Index) == index && a.OperationName == "search " + operation)];

    public static TheoryData<string, string> Families => new()
    {
        { "index", SearchOperations.Index },
        { "index_many", SearchOperations.IndexMany },
        { "delete", SearchOperations.Delete },
        { "delete_many", SearchOperations.DeleteMany },
        { "delete_by_filter", SearchOperations.DeleteByFilter },
        { "clear", SearchOperations.Clear },
        { "search", SearchOperations.Search },
        { "get", SearchOperations.Get },
        { "count", SearchOperations.Count },
        { "enumerate", SearchOperations.Enumerate },
        { "instant", SearchOperations.InstantSearch },
        { "facet_values", SearchOperations.SearchFacetValues },
        { "ensure_index", SearchOperations.EnsureIndex },
        { "index_exists", SearchOperations.IndexExists },
        { "delete_index", SearchOperations.DeleteIndex },
        { "cutover", SearchOperations.Cutover },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public async Task AnOutage_IsARecordedFailure_OnEveryOperationFamily(string family, string operation)
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(
            new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        Error? error = await MeiliOperations.All[family](harness, CancellationToken.None);

        error.Should().NotBeNull();
        error!.Code.Should().Be(SearchErrorCodes.Unavailable);
        List<Activity> recorded = SpansOf(spans, index, operation);
        recorded.Should().NotBeEmpty($"'{operation}' starts a span");
        Activity span = recorded[0];
        span.ShouldBeRecordedFailure(error)
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveSource(SearchWellKnown.ActivitySourceName)
            .ShouldHaveTag(SearchTagKeys.Provider, SearchWellKnown.MeilisearchProviderName)
            .ShouldHaveTag(WellKnownTagKeys.ErrorType, "Unavailable")
            .ShouldHaveTag(WellKnownTagKeys.ErrorCode, SearchErrorCodes.Unavailable);
        span.OperationName.Should().Be("search " + operation).And.Be(span.OperationName.ToLowerInvariant());
        span.TagObjects.Select(t => t.Key).Should().BeEquivalentTo(
            [.. SpanTags, WellKnownTagKeys.ErrorCode, WellKnownTagKeys.ErrorType], $"'{operation}' tags only the shared keys");

        RecordedMeasurement duration = DurationsOf(metrics, index, operation).First();
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Failure);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be("Unavailable");
        Tag(duration, SearchTagKeys.Provider).Should().Be(SearchWellKnown.MeilisearchProviderName);
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo(
            [.. SpanTags, WellKnownTagKeys.Outcome, WellKnownTagKeys.ErrorType]);
        spans.StartedActivities.Where(a => (string?)a.GetTagItem(SearchTagKeys.Index) == index).Should().OnlyContain(a => a.IsStopped, "every span is ended");
    }

    [Fact]
    public async Task ASuccessfulWrite_RecordsAnUnsetSpan_ADuration_AndTheDocumentCount()
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Ok(EnqueuedTask)), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        var result = await harness.Index.IndexAsync(MeiliFaultHarness.Doc(), SearchWriteConsistency.Accepted);

        result.IsSuccess.Should().BeTrue();
        SpansOf(spans, index, SearchOperations.Index).Should().ContainSingle().Which
            .ShouldBeRecordedSuccess()
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldHaveTagKeys(SpanTags);
        RecordedMeasurement duration = DurationsOf(metrics, index, SearchOperations.Index).Should().ContainSingle().Subject;
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Success);
        duration.Value.Should().BeGreaterThan(0);
        duration.Tags.Select(t => t.Key).Should().BeEquivalentTo([.. SpanTags, WellKnownTagKeys.Outcome]);

        RecordedMeasurement documents = metrics.MeasurementsOf(Documents)
            .Where(m => Tag(m, SearchTagKeys.Index) == index).Should().ContainSingle().Subject;
        documents.Value.Should().Be(1);
        documents.Tags.Select(t => t.Key).Should().BeEquivalentTo(SpanTags);
    }

    [Fact]
    public async Task InstrumentsFollowTheNamingUnitAndOutcomeConventions()
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Ok(EnqueuedTask)), indexName: index);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        await harness.Index.IndexAsync(MeiliFaultHarness.Doc(), SearchWriteConsistency.Accepted);

        metrics.Instruments.Where(i => i.Name == Duration).Should().NotBeEmpty().And.OnlyContain(i => i.Unit == "s" && i.Kind == InstrumentKind.Histogram);
        metrics.Instruments.Where(i => i.Name == Documents).Should().NotBeEmpty().And.OnlyContain(i => i.Unit == "{document}" && i.Kind == InstrumentKind.Counter);
        metrics.Instruments.Select(i => i.MeterName).Distinct().Should().Equal(SearchWellKnown.MeterName);
        metrics.ShouldUseOutcomeVocabulary(Duration);
    }

    [Fact]
    public void TheSourceVersion_IsTheAssemblyInformationalVersion()
    {
        SearchTelemetry.Source.Version.Should().Be(TelemetryVersion.Of(typeof(SearchTelemetry).Assembly));
        SearchTelemetry.Source.Name.Should().Be(SearchWellKnown.ActivitySourceName);
    }

    [Fact]
    public async Task AnUnclassifiedException_RecordsTheExceptionEvent_AndTheFullTypeNameAsErrorType()
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(
            new ScriptedHttpHandler(HttpReply.Throws(new InvalidOperationException("never a tag"))), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        var act = () => harness.Index.CountAsync(null, MeiliOperations.Tenant);
        await act.Should().ThrowAsync<InvalidOperationException>();

        SpansOf(spans, index, SearchOperations.Count).Should().ContainSingle().Which
            .ShouldBeRecordedException(typeof(InvalidOperationException));
        RecordedMeasurement duration = DurationsOf(metrics, index, SearchOperations.Count).Should().ContainSingle().Subject;
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Exception);
        Tag(duration, WellKnownTagKeys.ErrorType).Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Theory]
    [MemberData(nameof(Families))]
    public async Task CancellingTheCallersToken_IsNeverAnError_AndStillRecordsTheDuration(string family, string operation)
    {
        string index = NewIndexName();
        var handler = new ScriptedHttpHandler(HttpReply.HangUntilCancelled());
        var harness = new MeiliFaultHarness(handler, indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);
        using var cts = new CancellationTokenSource();

        var pending = MeiliOperations.All[family](harness, cts.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        var act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();

        List<Activity> recorded = SpansOf(spans, index, operation);
        recorded.Should().NotBeEmpty();
        recorded[0].ShouldBeRecordedCancellation();
        RecordedMeasurement duration = DurationsOf(metrics, index, operation).First();
        Tag(duration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Canceled);
        duration.Tags.Select(t => t.Key).Should().NotContain(WellKnownTagKeys.ErrorType);
    }

    [Fact]
    public async Task ARequestRefusedBeforeAnyIo_IsStillARecordedFailure_WithoutTheOffendingValue()
    {
        string index = NewIndexName();
        var handler = new ScriptedHttpHandler(HttpReply.Ok());
        var harness = new MeiliFaultHarness(handler, indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        var result = await harness.Index.SearchAsync(SearchRequest.Default, TenantScope.Global);

        result.Error.Code.Should().Be(SearchErrorCodes.TenantScopeMissing);
        handler.Requests.Should().BeEmpty();
        SpansOf(spans, index, SearchOperations.Search).Should().ContainSingle().Which.ShouldBeRecordedFailure(result.Error);
        Tag(DurationsOf(metrics, index, SearchOperations.Search).Single(), WellKnownTagKeys.ErrorType).Should().Be("Unauthorized");
    }

    [Fact]
    public async Task NoQueryText_FilterValue_DocumentId_OrTenant_BecomesATag()
    {
        const string sentinel = "SENTINEL-4f2a";
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(
            new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);
        var request = SearchRequest.Default with { FreeText = sentinel, Filter = SearchFilter.Eq("status", sentinel) };

        await harness.Index.SearchAsync(request, MeiliOperations.Tenant);
        await harness.Index.GetAsync("doc-" + sentinel, MeiliOperations.Tenant);
        await harness.Index.DeleteAsync("doc-" + sentinel, SearchWriteConsistency.Accepted);

        spans.RecordedActivities.Should().NotBeEmpty();
        spans.RecordedActivities.ShouldNotLeak(sentinel, TestTenants.TenantA.ToString(), MeiliFaultHarness.HostSentinel);
        metrics.ShouldNotLeak(sentinel, TestTenants.TenantA.ToString(), MeiliFaultHarness.HostSentinel);
    }

    [Fact]
    public async Task TenantTokenIssuance_IsAnInternalSpan_WithoutIo()
    {
        string index = NewIndexName();
        var handler = new ScriptedHttpHandler(HttpReply.Ok());
        var harness = new MeiliFaultHarness(handler, indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);
        using var metrics = MetricRecorder.StartRecording(SearchWellKnown.MeterName);

        var result = await harness.TokenIssuer.IssueAsync(TenantScope.Global, "tenantId", [index], TimeSpan.FromMinutes(5));

        result.Error.Code.Should().Be(SearchErrorCodes.TenantScopeMissing);
        handler.Requests.Should().BeEmpty();
        SpansOf(spans, index, SearchOperations.IssueTenantToken).Should().ContainSingle().Which
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldBeRecordedFailure(result.Error);
        Tag(DurationsOf(metrics, index, SearchOperations.IssueTenantToken).Single(), WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Failure);
    }

    [Fact]
    public async Task VerifyingTheRegisteredIndexes_IsAClientSpan_NamedAfterTheOperation()
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(
            new ScriptedHttpHandler(HttpReply.Throws(new HttpRequestException("refused"))), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);

        var result = await harness.Provisioner.VerifyRegisteredIndexesAsync();

        result.IsFailure.Should().BeTrue();
        SpansOf(spans, index, SearchOperations.VerifyIndexes).Should().ContainSingle().Which
            .ShouldHaveKind(ActivityKind.Client)
            .ShouldBeRecordedFailure(result.Error);
    }

    [Fact]
    public async Task TheReadinessProbe_StartsNoSpan()
    {
        string index = NewIndexName();
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(HttpReply.Ok()), indexName: index);
        using var spans = ActivityRecorder.StartRecording(SearchWellKnown.ActivitySourceName);

        await harness.Provisioner.ProbeAsync(index);

        spans.StartedActivities.Where(a => (string?)a.GetTagItem(SearchTagKeys.Index) == index).Should().BeEmpty();
    }

    [Fact]
    public void TheSourceAndMeterName_AreAPublicConstOfThisAssembly_EqualToTheSharedName()
    {
        typeof(SearchTelemetry).GetField(nameof(SearchTelemetry.Name), BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue().Should().Be(SearchWellKnown.ActivitySourceName).And.Be(SearchWellKnown.MeterName);
    }
}
