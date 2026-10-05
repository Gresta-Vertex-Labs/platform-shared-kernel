using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Testing.Clocks;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Index;

/// <summary>
/// Tests that this provider emits OpenTelemetry spans and measurements under the same
/// <c>SharedKernel.Search</c> source and meter names the Meilisearch sibling uses.
/// </summary>
/// <remarks>
/// <para>
/// Before the pre-publish pass both provider packages declared an <see cref="ActivitySource"/> and a
/// <see cref="Meter"/> and never called either — zero spans, zero instruments, domain-wide — while
/// <c>13.ServiceDefaults</c> shipped a <c>WithSearchTelemetry()</c> that wired both by name. That
/// cross-domain contract was documented, wired, and completely inert.
/// </para>
/// <para>
/// Byte-identical naming across the two providers is the whole reason one <c>WithSearchTelemetry()</c>
/// call can cover both with no <c>ProjectReference</c> to <c>09.Search</c>, so it is asserted here as
/// well as in the Meilisearch suite — a rename in one package alone would silently halve the telemetry
/// a service receives after a provider swap.
/// </para>
/// </remarks>
public sealed class ElasticSearchTelemetryTests
{
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
    private const string TelemetryIndexName = "products-telemetry-elasticsearch";

    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static ElasticSearchIndex<TestDocument> CreateIndexWithNoIoCapableClient(string indexName) =>
        new(
            client: null!,
            new SearchIndexDefinitionBuilder(indexName)
                .Field("name", SearchFieldKind.Text, searchable: true)
                .Build()
                .Value,
            writeAlias: indexName,
            new ElasticSearchOptions { Nodes = ["http://127.0.0.1:1/"] },
            new FakeClock(),
            NullLogger<ElasticSearchIndex<TestDocument>>.Instance);

    [Fact]
    public async Task SearchAsync_EmitsAClientSpanTaggedWithIndexOperationAndProvider()
    {
        const string indexName = TelemetryIndexName + "-span";
        var activities = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
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
            ActivityStopped = activities.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        Activity.Current = null;

        var index = CreateIndexWithNoIoCapableClient(indexName);
        await index.SearchAsync(SearchRequest.Default, TenantScope.Global);

        var activity = activities
            .Where(a => (string?)a.GetTagItem(SearchWellKnown.IndexTagName) == indexName)
            .Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("search search");
        activity.Kind.Should().Be(ActivityKind.Client);
        activity.GetTagItem(SearchWellKnown.IndexTagName).Should().Be(indexName);
        activity.GetTagItem(SearchWellKnown.OperationTagName).Should().Be("search");
        activity.GetTagItem(SearchWellKnown.ProviderTagName).Should().Be(SearchWellKnown.ElasticSearchProviderName);
        activity.Status.Should().Be(ActivityStatusCode.Error, "this index has no usable client");
    }

    [Fact]
    public async Task SearchAsync_RecordsTheOperationDurationHistogramUnderTheSharedMeterName()
    {
        const string indexName = TelemetryIndexName + "-histogram";
        var values = new List<double>();
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
            foreach (var tag in tags)
            {
                if (tag.Key == SearchWellKnown.IndexTagName && (string?)tag.Value == indexName)
                {
                    values.Add(value);
                    return;
                }
            }
        });
        Activity.Current = null;

        listener.Start();

        var index = CreateIndexWithNoIoCapableClient(indexName);
        await index.SearchAsync(SearchRequest.Default, TenantScope.Global);

        values.Should().ContainSingle().Which.Should().BeGreaterThan(0, "the histogram records elapsed seconds");
    }
}
