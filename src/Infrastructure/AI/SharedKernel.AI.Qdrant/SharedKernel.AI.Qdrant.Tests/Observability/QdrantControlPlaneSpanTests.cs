using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Quantization;
using SharedKernel.AI.Qdrant.Tests.Conformance;
using SharedKernel.AI.Qdrant.Tests.Support;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.Qdrant.Tests.Observability;

/// <summary>
/// The control plane against a real Qdrant: ensure, exists, cutover, quantization profile and delete each produce one
/// Client span <c>ai {operation}</c> with the exact three tag keys and one duration observation, with the same
/// vocabulary as the data plane. Part of the shared conformance collection.
/// </summary>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantControlPlaneSpanTests : IAsyncLifetime
{
    private readonly QdrantContainerFixture _fixture;
    private readonly string _staging = $"cp-staging-{Guid.NewGuid():N}";
    private readonly string _live = $"cp-live-{Guid.NewGuid():N}";
    private QdrantClient _client = null!;
    private QdrantCollectionProvisioner _provisioner = null!;
    private QdrantQuantizationProfileAccessor _quantization = null!;
    private ActivityRecorder _activities = null!;
    private MetricRecorder _metrics = null!;

    public QdrantControlPlaneSpanTests(QdrantContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        var uri = new Uri(_fixture.GrpcEndpoint);
        _client = new QdrantClient(uri.Host, uri.Port);
        var telemetry = TestMeterFactory.NewTelemetry();
        _provisioner = new QdrantCollectionProvisioner(_client, telemetry, NullLogger<QdrantCollectionProvisioner>.Instance);
        _quantization = new QdrantQuantizationProfileAccessor(_client, telemetry, NullLogger<QdrantQuantizationProfileAccessor>.Instance);
        _activities = ActivityRecorder.StartRecording(IntelligenceWellKnown.ActivitySourceName);
        _metrics = MetricRecorder.StartRecording(IntelligenceWellKnown.MeterName);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _provisioner.DeleteCollectionAsync(_staging);
        await _provisioner.DeleteCollectionAsync(_live);
        _activities.Dispose();
        _metrics.Dispose();
        _client.Dispose();
    }

    private static string? Tag(RecordedMeasurement measurement, string key) =>
        measurement.TryGetTag(key, out var value) ? (string?)value : null;

    private VectorCollectionDefinition Definition(string name) =>
        new VectorCollectionDefinitionBuilder(name)
            .EmbeddingModel("model-a", 2)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build()
            .Value;

    [Fact]
    public async Task EveryControlPlaneOperation_ProducesOneClientSpan_WithTheExactTags_AndOneSuccessDuration()
    {
        (await _provisioner.EnsureCollectionAsync(Definition(_staging))).IsSuccess.Should().BeTrue();
        (await _provisioner.CollectionExistsAsync(_staging)).IsSuccess.Should().BeTrue();
        (await _quantization.GetQuantizationProfileAsync(_staging)).IsSuccess.Should().BeTrue();
        (await _provisioner.CutoverAsync(new VectorCollectionCutoverRequest
        {
            StagingCollectionName = _staging,
            LiveCollectionName = _live,
        })).IsSuccess.Should().BeTrue();
        (await _provisioner.DeleteCollectionAsync(_staging)).IsSuccess.Should().BeTrue();

        var collections = new[] { _staging, _live };
        var spans = _activities.RecordedActivities
            .Where(a => collections.Contains(a.GetTagItem(IntelligenceTagKeys.Collection) as string))
            .ToList();

        spans.Select(s => s.OperationName).Should().BeEquivalentTo(
        [
            $"ai {IntelligenceOperations.EnsureCollection}",
            $"ai {IntelligenceOperations.CollectionExists}",
            $"ai {IntelligenceOperations.QuantizationProfile}",
            $"ai {IntelligenceOperations.Cutover}",
            $"ai {IntelligenceOperations.DeleteCollection}",
        ]);

        foreach (var span in spans)
        {
            span.ShouldHaveKind(ActivityKind.Client)
                .ShouldHaveSource(IntelligenceWellKnown.ActivitySourceName)
                .ShouldHaveTagKeys(IntelligenceTagKeys.Provider, IntelligenceTagKeys.Operation, IntelligenceTagKeys.Collection)
                .ShouldHaveTag(IntelligenceTagKeys.Provider, IntelligenceWellKnown.QdrantProviderName)
                .ShouldBeRecordedSuccess();
        }

        spans.Single(s => s.OperationName == $"ai {IntelligenceOperations.Cutover}")
            .GetTagItem(IntelligenceTagKeys.Collection).Should().Be(_live, "cutover is one span tagged with the live collection");
        spans.ShouldFollowSpanConvention();

        var durations = _metrics.MeasurementsOf(IntelligenceWellKnown.OperationDurationInstrumentName)
            .Where(m => collections.Contains(Tag(m, IntelligenceTagKeys.Collection)))
            .ToList();
        durations.Select(m => Tag(m, IntelligenceTagKeys.Operation)).Should().BeEquivalentTo(
        [
            IntelligenceOperations.EnsureCollection,
            IntelligenceOperations.CollectionExists,
            IntelligenceOperations.QuantizationProfile,
            IntelligenceOperations.Cutover,
            IntelligenceOperations.DeleteCollection,
        ]);
        durations.Should().OnlyContain(m => Tag(m, WellKnownTagKeys.Outcome) == WellKnownOutcomes.Success);
        _metrics.ShouldFollowMetricConvention();
    }

    [Fact]
    public async Task ARejectedCutover_IsAFailedSpan_ForTheLiveCollection()
    {
        var result = await _provisioner.CutoverAsync(new VectorCollectionCutoverRequest
        {
            StagingCollectionName = _staging,
            LiveCollectionName = _live,
        });

        result.IsFailure.Should().BeTrue();
        _activities.RecordedActivities
            .Where(a => a.GetTagItem(IntelligenceTagKeys.Collection) as string == _live)
            .Should().ContainSingle()
            .Which.ShouldBeRecordedFailure(result.Error);
    }
}
