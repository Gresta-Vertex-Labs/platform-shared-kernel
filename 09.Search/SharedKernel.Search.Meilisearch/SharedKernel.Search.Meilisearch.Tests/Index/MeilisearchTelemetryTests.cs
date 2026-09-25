using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Index;

/// <summary>
/// Tests that this provider actually emits OpenTelemetry spans and measurements.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these protect against.</b> Before the pre-publish pass both provider packages declared a
/// <see cref="ActivitySource"/> and a <see cref="Meter"/> named <c>SharedKernel.Search</c> — and never
/// called either. There were zero <c>StartActivity</c> calls and zero instruments in the entire domain,
/// while <c>13.ServiceDefaults</c> shipped a <c>WithSearchTelemetry()</c> that dutifully wired both by
/// name. A consuming service could enable search telemetry, see no error, and get no data.
/// </para>
/// <para>
/// These tests subscribe by the same string name <c>13.ServiceDefaults</c> uses, so they fail if the
/// instrumentation is removed, renamed, or stops being reached — the failure mode that produced an
/// inert cross-domain contract in the first place.
/// </para>
/// </remarks>
public sealed class MeilisearchTelemetryTests
{
    private const string UnreachableUrl = "http://127.0.0.1:1/";

    /// <summary>
    /// A dedicated index name used only by this class. ActivitySource and Meter are process-global, so a
    /// listener started here also observes every other test class running in parallel — asserting on an
    /// unfiltered collection would make these tests flaky for reasons that have nothing to do with the
    /// instrumentation being tested.
    /// </summary>
    // One index name PER TEST, not per class: ActivitySource and Meter are process-global, and the
    // assertions below filter on the index tag to ignore other classes running in parallel. Two methods
    // sharing a name would defeat that filter against each other and make these tests flaky for reasons
    // unrelated to the instrumentation they cover.
    private const string TelemetryIndexName = "products-telemetry-meilisearch";

    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static MeilisearchIndex<TestDocument> CreateIndex(string indexName)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(UnreachableUrl), Timeout = TimeSpan.FromSeconds(1) };
        return new MeilisearchIndex<TestDocument>(
            new global::Meilisearch.MeilisearchClient(httpClient, "test-key"),
            new SearchIndexDefinitionBuilder(indexName)
                .Field("name", SearchFieldKind.Text, searchable: true)
                .Build()
                .Value,
            new MeilisearchOptions { Url = UnreachableUrl, TaskWaitTimeoutSeconds = 1 },
            new FakeClock(),
            NullLogger<MeilisearchIndex<TestDocument>>.Instance);
    }

    [Fact]
    public async Task SearchAsync_EmitsAClientSpanOnTheSharedSearchActivitySource()
    {
        const string indexName = TelemetryIndexName + "-span";
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SearchWellKnown.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // Both sampling callbacks are set, and the ambient Activity is cleared below. A listener that
            // implements only Sample records nothing when the activity being created has a hierarchical
            // parent id, and Activity.Current can be non-null here because the ElasticSearch client's own
            // transport instrumentation runs in other test classes executing in parallel. Without both,
            // this test passes alone and fails in a full run.
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;

        var index = CreateIndex(indexName);
        await index.SearchAsync(SearchRequest.Default, TenantScope.None);

        var activity = activities
            .Where(a => (string?)a.GetTagItem(SearchWellKnown.IndexTagName) == indexName)
            .Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("search search");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(SearchWellKnown.IndexTagName).Should().Be(indexName);
        activity.GetTagItem(SearchWellKnown.OperationTagName).Should().Be("search");
        activity.GetTagItem(SearchWellKnown.ProviderTagName).Should().Be(SearchWellKnown.MeilisearchProviderName);
    }

    [Fact]
    public async Task AFailedOperation_MarksTheSpanFailed_AndTagsTheErrorCode()
    {
        const string indexName = TelemetryIndexName + "-failure";
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SearchWellKnown.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // Both sampling callbacks are set, and the ambient Activity is cleared below. A listener that
            // implements only Sample records nothing when the activity being created has a hierarchical
            // parent id, and Activity.Current can be non-null here because the ElasticSearch client's own
            // transport instrumentation runs in other test classes executing in parallel. Without both,
            // this test passes alone and fails in a full run.
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;

        var index = CreateIndex(indexName);
        var result = await index.SearchAsync(SearchRequest.Default, TenantScope.None);
        result.IsFailure.Should().BeTrue("this index points at an unreachable instance");

        var activity = activities
            .Where(a => (string?)a.GetTagItem(SearchWellKnown.IndexTagName) == indexName)
            .Should().ContainSingle().Subject;
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem("error.type").Should().Be(result.Error.Code);
    }

    [Fact]
    public async Task SearchAsync_RecordsTheOperationDurationHistogram()
    {
        const string indexName = TelemetryIndexName + "-histogram";
        var measurements = new List<(double Value, string? Index, string? Operation, string? Provider)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == SearchWellKnown.MeterName
                    && instrument.Name == SearchWellKnown.OperationDurationMetricName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            // Copied out of the ref-struct span before anything else touches it: a ReadOnlySpan<T>
            // parameter cannot be captured by a local function or lambda.
            string? index = null;
            string? operation = null;
            string? provider = null;
            foreach (var tag in tags)
            {
                if (tag.Key == SearchWellKnown.IndexTagName)
                {
                    index = tag.Value?.ToString();
                }
                else if (tag.Key == SearchWellKnown.OperationTagName)
                {
                    operation = tag.Value?.ToString();
                }
                else if (tag.Key == SearchWellKnown.ProviderTagName)
                {
                    provider = tag.Value?.ToString();
                }
            }

            measurements.Add((value, index, operation, provider));
        });
        Activity.Current = null;

        listener.Start();

        var index = CreateIndex(indexName);
        await index.SearchAsync(SearchRequest.Default, TenantScope.None);

        var measurement = measurements.Where(m => m.Index == indexName).Should().ContainSingle().Subject;
        measurement.Value.Should().BeGreaterThan(0, "the histogram records elapsed seconds");
        measurement.Index.Should().Be(indexName);
        measurement.Operation.Should().Be("search");
        measurement.Provider.Should().Be(SearchWellKnown.MeilisearchProviderName);
    }

    [Fact]
    public void TheMeterAndActivitySourceAreNamedExactlyWhatServiceDefaultsWires()
    {
        // 13.ServiceDefaults' WithSearchTelemetry() subscribes by these bare string names and takes no
        // ProjectReference to 09.Search, so the names are the entire contract between the two domains.
        // The ElasticSearch sibling declares its own instances under the identical names, which is what
        // lets one WithSearchTelemetry() call cover both providers.
        SearchWellKnown.ActivitySourceName.Should().Be("SharedKernel.Search");
        SearchWellKnown.MeterName.Should().Be("SharedKernel.Search");
    }
}
