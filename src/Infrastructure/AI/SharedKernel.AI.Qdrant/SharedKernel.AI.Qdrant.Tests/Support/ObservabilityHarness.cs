using System.Diagnostics;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.AI.Qdrant.Tests.Support;

/// <summary>
/// One collection over a client with the recorders attached. The recorders are process-wide, so every assertion filters
/// by this harness's unique collection name.
/// </summary>
internal sealed class ObservabilityHarness : IDisposable
{
    public const string ModelId = "model-a";

    public ObservabilityHarness(IQdrantClient client, bool tenanted = false, string? name = null)
    {
        Name = name ?? UniqueNames.Of("obs");
        Client = client;
        Logger = new InMemoryLogger<QdrantVectorCollection<TestVectorRecord>>();
        Activities = ActivityRecorder.StartRecording(IntelligenceWellKnown.ActivitySourceName);
        Metrics = MetricRecorder.StartRecording(IntelligenceWellKnown.MeterName);
        Telemetry = TestMeterFactory.NewTelemetry();

        var builder = new VectorCollectionDefinitionBuilder(Name).EmbeddingModel(ModelId, 2).DistanceMetric(VectorDistanceMetric.Cosine);
        if (tenanted)
        {
            builder.TenantField("tenantId").Field("tenantId", VectorFieldKind.String, filterable: true);
        }

        Definition = builder.Field("category", VectorFieldKind.String, filterable: true).Build().Value;
        Collection = new QdrantVectorCollection<TestVectorRecord>(client, Definition, new FakeClock(), Telemetry, Logger);
    }

    public string Name { get; }

    public IQdrantClient Client { get; }

    public VectorCollectionDefinition Definition { get; }

    public QdrantVectorCollection<TestVectorRecord> Collection { get; }

    public InMemoryLogger<QdrantVectorCollection<TestVectorRecord>> Logger { get; }

    public ActivityRecorder Activities { get; }

    public MetricRecorder Metrics { get; }

    public QdrantTelemetry Telemetry { get; }

    /// <summary>Gets the recorded spans of this harness's collection.</summary>
    public IReadOnlyList<Activity> Spans =>
        [.. Activities.RecordedActivities.Where(a => a.GetTagItem(IntelligenceTagKeys.Collection) as string == Name)];

    /// <summary>Gets this harness's measurements of <paramref name="instrument"/>.</summary>
    public IReadOnlyList<RecordedMeasurement> Measurements(string instrument) =>
        [.. Metrics.MeasurementsOf(instrument).Where(m => m.TryGetTag(IntelligenceTagKeys.Collection, out var c) && (string?)c == Name)];

    public TestVectorRecord Record(string id, string? tenant = null) => new()
    {
        Id = id,
        ModelId = ModelId,
        Vector = new float[] { 0.1f, 0.2f },
        Metadata = new Dictionary<string, VectorValue> { ["category"] = VectorValue.From("sentinel-category") },
    };

    public void Dispose()
    {
        Activities.Dispose();
        Metrics.Dispose();
    }

    /// <summary>One public operation of <see cref="IVectorCollection{TRecord}"/> reduced to "the error it failed with, if any".</summary>
    public delegate Task<Error?> Operation(ObservabilityHarness harness, TenantScope tenant, CancellationToken cancellationToken);

    /// <summary>Every data-plane operation family, by its <see cref="IntelligenceOperations"/> name.</summary>
    public static IReadOnlyDictionary<string, Operation> Operations { get; } = new Dictionary<string, Operation>(StringComparer.Ordinal)
    {
        [IntelligenceOperations.Upsert] = async (h, t, ct) => Failure(await h.Collection.UpsertAsync(h.Record("1"), t, ct)),
        [IntelligenceOperations.UpsertMany] = async (h, t, ct) => Failure(await h.Collection.UpsertManyAsync([h.Record("1"), h.Record("2")], t, ct)),
        [IntelligenceOperations.Delete] = async (h, t, ct) => Failure(await h.Collection.DeleteAsync("1", t, ct)),
        [IntelligenceOperations.DeleteMany] = async (h, t, ct) => Failure(await h.Collection.DeleteManyAsync(["1", "2"], t, ct)),
        [IntelligenceOperations.DeleteByFilter] = async (h, t, ct) =>
            Failure(await h.Collection.DeleteByFilterAsync(VectorFilter.Eq("category", VectorValue.From("sentinel-filter-value")), t, ct)),
        [IntelligenceOperations.Query] = async (h, t, ct) => Failure(await h.Collection.QueryAsync(
            new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = ModelId, Limit = 3, Filter = VectorFilter.Eq("category", VectorValue.From("sentinel-filter-value")) }, t, ct)),
        [IntelligenceOperations.Get] = async (h, t, ct) => Failure(await h.Collection.GetAsync("1", t, ct)),
        [IntelligenceOperations.Count] = async (h, t, ct) => Failure(await h.Collection.CountAsync(null, t, ct)),
        [IntelligenceOperations.Scroll] = async (h, t, ct) =>
        {
            try
            {
                await foreach (var _ in h.Collection.ScrollAsync(null, t, batchSize: 2, ct))
                {
                }

                return null;
            }
            catch (IntelligenceStreamException ex)
            {
                return ex.Error;
            }
        },
    };

    private static Error? Failure<T>(Result<T> result) => result.IsFailure ? result.Error : null;
}
