using System.Diagnostics;
using FluentAssertions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Tests.Conformance;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>
/// The success paths of every data-plane operation against a real Qdrant: one Client span and one duration observation
/// each, the exact tag-key set, no content in any tag, the record counter, and a real cancelled walk. Part of the shared
/// conformance collection, so a future vector provider can run the same assertions.
/// </summary>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantContainerObservabilityTests : IAsyncLifetime
{
    private const string SentinelRecordId = "424242";
    private const string SentinelCategory = "sentinel-category";
    private const string SentinelFilter = "sentinel-filter-value";

    private readonly QdrantContainerFixture _fixture;
    private readonly string _name = UniqueNames.Of("obs");
    private QdrantClient _client = null!;
    private QdrantCollectionProvisioner _provisioner = null!;
    private ObservabilityHarness _harness = null!;

    public QdrantContainerObservabilityTests(QdrantContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var uri = new Uri(_fixture.GrpcEndpoint);
        _client = new QdrantClient(uri.Host, uri.Port);
        _harness = new ObservabilityHarness(_client, name: _name);
        _provisioner = new QdrantCollectionProvisioner(_client, TestMeterFactory.NewTelemetry(), NullLogger<QdrantCollectionProvisioner>.Instance);
        (await _provisioner.EnsureCollectionAsync(_harness.Definition)).IsSuccess.Should().BeTrue();
        _harness.Activities.Clear();
        _harness.Metrics.Clear();
    }

    public async Task DisposeAsync()
    {
        await _provisioner.DeleteCollectionAsync(_name);
        _harness.Dispose();
        _client.Dispose();
    }

    private ObservabilityHarness.Operation Op(string name) => ObservabilityHarness.Operations[name];

    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    private TestVectorRecord Rec(string id) => _harness.Record(id);

    [Fact]
    public async Task EveryDataPlaneOperation_ProducesOneClientSpan_WithTheExactTags_AndOneSuccessDuration()
    {
        await _harness.Collection.UpsertAsync(Rec("1"), TenantScope.Global);
        await _harness.Collection.UpsertManyAsync([Rec("2"), Rec("3")], TenantScope.Global);
        await _harness.Collection.GetAsync("1", TenantScope.Global);
        await _harness.Collection.CountAsync(null, TenantScope.Global);
        await _harness.Collection.QueryAsync(
            new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = ObservabilityHarness.ModelId, Limit = 3, Filter = VectorFilter.Eq("category", VectorValue.From(SentinelFilter)) },
            TenantScope.Global);
        await foreach (var _ in _harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 2))
        {
        }

        await _harness.Collection.DeleteByFilterAsync(VectorFilter.Eq("category", VectorValue.From(SentinelFilter)), TenantScope.Global);
        await _harness.Collection.DeleteAsync("1", TenantScope.Global);
        await _harness.Collection.DeleteManyAsync(["2", "3"], TenantScope.Global);

        var expected = new[]
        {
            IntelligenceOperations.Upsert, IntelligenceOperations.UpsertMany, IntelligenceOperations.Get, IntelligenceOperations.Count,
            IntelligenceOperations.Query, IntelligenceOperations.Scroll, IntelligenceOperations.DeleteByFilter,
            IntelligenceOperations.Delete, IntelligenceOperations.DeleteMany,
        };

        var spans = _harness.Spans;
        spans.Select(s => s.OperationName).Should().BeEquivalentTo(expected.Select(o => $"ai {o}"));
        foreach (var span in spans)
        {
            span.ShouldHaveKind(ActivityKind.Client)
                .ShouldHaveSource(IntelligenceWellKnown.ActivitySourceName)
                .ShouldHaveTagKeys(IntelligenceTagKeys.Provider, IntelligenceTagKeys.Operation, IntelligenceTagKeys.Collection)
                .ShouldHaveTag(IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName)
                .ShouldHaveTag(IntelligenceTagKeys.Collection, _name)
                .ShouldBeRecordedSuccess();
        }

        spans.ShouldFollowSpanConvention();
        spans.ShouldNotLeak(SentinelRecordId, SentinelCategory, SentinelFilter, "0.1", "0.2");

        var durations = _harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName);
        durations.Should().HaveCount(expected.Length);
        durations.Select(m => Tag(m, IntelligenceTagKeys.Operation))
            .Should().BeEquivalentTo(expected);
        durations.Should().OnlyContain(m => Tag(m, WellKnownTagKeys.Outcome) == WellKnownOutcomes.Success);
        durations.Should().OnlyContain(m => Tag(m, WellKnownTagKeys.ErrorType) == null);
        durations.Should().OnlyContain(m => m.Value >= 0);
    }

    [Fact]
    public async Task Writes_CountTheirRecords_BySuccess()
    {
        await _harness.Collection.UpsertAsync(Rec("11"), TenantScope.Global);
        await _harness.Collection.UpsertManyAsync([Rec("12"), Rec("13"), Rec("14")], TenantScope.Global);
        await _harness.Collection.DeleteAsync("11", TenantScope.Global);
        await _harness.Collection.DeleteManyAsync(["12", "13"], TenantScope.Global);
        await _harness.Collection.UpsertAsync(Rec("not-a-legal-id"), TenantScope.Global);

        long Sum(string operation, string outcome) => _harness.Measurements(IntelligenceWellKnown.VectorRecordsInstrumentName)
            .Where(m => Tag(m, IntelligenceTagKeys.Operation) == operation && Tag(m, WellKnownTagKeys.Outcome) == outcome)
            .Sum(m => (long)m.Value);

        Sum(IntelligenceOperations.Upsert, WellKnownOutcomes.Success).Should().Be(1);
        Sum(IntelligenceOperations.UpsertMany, WellKnownOutcomes.Success).Should().Be(3);
        Sum(IntelligenceOperations.Delete, WellKnownOutcomes.Success).Should().Be(1);
        Sum(IntelligenceOperations.DeleteMany, WellKnownOutcomes.Success).Should().Be(2);
        Sum(IntelligenceOperations.Upsert, WellKnownOutcomes.Failure).Should().Be(1, "the rejected upsert counts as a failed record");
    }

    [Fact]
    public async Task ACancelledLongWalk_IsOperationCanceled_LeavesTheSpanUnset_AndKeepsTheConsumersActivity()
    {
        await _harness.Collection.UpsertManyAsync(Enumerable.Range(100, 30).Select(i => Rec(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList(), TenantScope.Global);
        _harness.Activities.Clear();
        using var consumer = new Activity("consumer").Start();
        using var cts = new CancellationTokenSource();
        var seen = 0;

        var act = async () =>
        {
            await foreach (var _ in _harness.Collection.ScrollAsync(null, TenantScope.Global, batchSize: 1, cts.Token))
            {
                Activity.Current.Should().BeSameAs(consumer);
                if (++seen == 2)
                {
                    await cts.CancelAsync();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().BeLessThan(30);
        Activity.Current.Should().BeSameAs(consumer);
        var span = _harness.Spans.Should().ContainSingle().Which;
        span.ShouldBeRecordedCancellation();
        var scrollDuration = _harness.Measurements(IntelligenceWellKnown.OperationDurationInstrumentName)
            .Where(m => Tag(m, IntelligenceTagKeys.Operation) == IntelligenceOperations.Scroll).Should().ContainSingle().Which;
        Tag(scrollDuration, WellKnownTagKeys.Outcome).Should().Be(WellKnownOutcomes.Canceled);
        _harness.Activities.StartedActivities.Where(a => a.GetTagItem(IntelligenceTagKeys.Collection) as string == _name).Should().OnlyContain(a => a.IsStopped);
    }

    [Fact]
    public async Task ARealCancelledQuery_IsOperationCanceled_NotAResult()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _harness.Collection.CountAsync(null, TenantScope.Global, cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cts.Token);
        _harness.Spans.Should().ContainSingle().Which.ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task ARealMissingCollection_IsANotFoundResult_WithAFailedSpan_AndNoEngineText()
    {
        var missing = new ObservabilityHarness(_client, name: UniqueNames.Of("missing"));
        using (missing)
        {
            var result = await missing.Collection.CountAsync(null, TenantScope.Global);

            result.Error.Code.Should().Be("intelligence.collection_not_found");
            result.Error.Message.Should().Contain(missing.Name).And.NotContain("doesn't exist");
            missing.Spans.Should().ContainSingle().Which.ShouldBeRecordedFailure(result.Error);
        }
    }
}
