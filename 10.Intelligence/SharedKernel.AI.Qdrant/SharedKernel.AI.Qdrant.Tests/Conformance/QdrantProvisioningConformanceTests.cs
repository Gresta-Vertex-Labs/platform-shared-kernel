using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.AI.Qdrant.Tests.Conformance;

/// <summary>
/// Real-backend provisioning conformance suite (T-04) — <see cref="QdrantCollectionProvisioner.EnsureCollectionAsync"/>
/// idempotency and additive-only behaviour, <c>Fingerprint</c> drift detection via
/// <see cref="QdrantCollectionProvisioner.ProbeAsync"/>, and the probe's healthy-plus-each-degraded-axis
/// shape (unreachable engine vs. reachable-but-unaddressable collection).
/// </summary>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantProvisioningConformanceTests : IAsyncLifetime
{
    private const string ModelId = "provisioning-model";

    private readonly QdrantContainerFixture _fixture;
    private readonly string _collectionName = $"provisioning-{Guid.NewGuid():N}";

    private QdrantClient _client = null!;
    private QdrantCollectionProvisioner _provisioner = null!;

    public QdrantProvisioningConformanceTests(QdrantContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        var grpcUri = new Uri(_fixture.GrpcEndpoint);
        _client = new QdrantClient(grpcUri.Host, grpcUri.Port);
        _provisioner = new QdrantCollectionProvisioner(_client, NullLogger<QdrantCollectionProvisioner>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _provisioner.DeleteCollectionAsync(_collectionName);
        }
        catch
        {
            // Best-effort — several tests delete the collection themselves already.
        }
    }

    private static VectorCollectionDefinition BaselineDefinition(string name) => new VectorCollectionDefinitionBuilder(name)
        .EmbeddingModel(ModelId, 3)
        .DistanceMetric(VectorDistanceMetric.Cosine)
        .Field("category", VectorFieldKind.String, filterable: true)
        .Build()
        .Value;

    [Fact]
    public async Task EnsureCollectionAsync_IsIdempotent_ReRunningWithTheIdenticalDefinitionSucceeds()
    {
        var definition = BaselineDefinition(_collectionName);

        var first = await _provisioner.EnsureCollectionAsync(definition);
        var second = await _provisioner.EnsureCollectionAsync(definition);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCollectionAsync_ConflictingDefinitionForAnExistingCollection_ReturnsCollectionDefinitionConflict_WithNoRewrite()
    {
        var original = BaselineDefinition(_collectionName);
        (await _provisioner.EnsureCollectionAsync(original)).IsSuccess.Should().BeTrue();

        var drifted = new VectorCollectionDefinitionBuilder(_collectionName)
            .EmbeddingModel(ModelId, 3)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Field("category", VectorFieldKind.String, filterable: true)
            .Field("newField", VectorFieldKind.Int64, filterable: true)
            .Build()
            .Value;

        var result = await _provisioner.EnsureCollectionAsync(drifted);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.collection_definition_conflict");

        // Prove the rejection is genuinely NO-REWRITE: the live schema fingerprint still matches the
        // ORIGINAL definition, never the drifted one — the remedy is staging -> Cutover, never a
        // silent in-place rewrite.
        var probe = await _provisioner.ProbeAsync(_collectionName);
        probe.Value.SchemaFingerprint.Should().Be(original.Fingerprint);
    }

    [Fact]
    public async Task ProbeAsync_DetectsSchemaFingerprint_MatchingTheProvisionedDefinition()
    {
        var definition = BaselineDefinition(_collectionName);
        (await _provisioner.EnsureCollectionAsync(definition)).IsSuccess.Should().BeTrue();

        var probe = await _provisioner.ProbeAsync(_collectionName);

        probe.IsSuccess.Should().BeTrue();
        probe.Value.Reachable.Should().BeTrue();
        probe.Value.CollectionAddressable.Should().BeTrue();
        probe.Value.Queryable.Should().BeTrue();
        probe.Value.SchemaFingerprint.Should().Be(definition.Fingerprint);
        probe.Value.VectorCount.Should().Be(0);
    }

    [Fact]
    public async Task ProbeAsync_VectorCount_ReflectsUpsertedRecords()
    {
        var definition = BaselineDefinition(_collectionName);
        (await _provisioner.EnsureCollectionAsync(definition)).IsSuccess.Should().BeTrue();

        var collection = new QdrantVectorCollection<TestVectorRecord>(
            _client, definition, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);
        var record = new TestVectorRecord { Id = "1", Vector = new float[] { 1, 0, 0 }, ModelId = ModelId };
        (await collection.UpsertAsync(record, TenantScope.Global)).IsSuccess.Should().BeTrue();

        var probe = await _provisioner.ProbeAsync(_collectionName);

        probe.Value.VectorCount.Should().Be(1);
    }

    [Fact]
    public async Task ProbeAsync_UnaddressableCollection_ReturnsReachableButNotAddressable_TheFirstDegradedAxis()
    {
        var probe = await _provisioner.ProbeAsync($"nonexistent-{Guid.NewGuid():N}");

        probe.IsSuccess.Should().BeTrue();
        probe.Value.Reachable.Should().BeTrue();
        probe.Value.CollectionAddressable.Should().BeFalse();
        probe.Value.Queryable.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_UnreachableEngine_ReturnsEverythingFalse_TheSecondDegradedAxis()
    {
        // A client pointed at a plainly unreachable address — distinguishes "cluster down" from the
        // "reachable but this collection is missing/mis-scoped" case exercised above.
        using var unreachableClient = new QdrantClient("127.0.0.1", 1, grpcTimeout: TimeSpan.FromSeconds(3));
        var unreachableProvisioner = new QdrantCollectionProvisioner(unreachableClient, NullLogger<QdrantCollectionProvisioner>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var probe = await unreachableProvisioner.ProbeAsync("anything", cts.Token);

        probe.IsSuccess.Should().BeTrue();
        probe.Value.Reachable.Should().BeFalse();
        probe.Value.CollectionAddressable.Should().BeFalse();
        probe.Value.Queryable.Should().BeFalse();
    }

    [Fact]
    public async Task CollectionExistsAsync_ReflectsProvisioningState()
    {
        (await _provisioner.CollectionExistsAsync(_collectionName)).Value.Should().BeFalse();

        await _provisioner.EnsureCollectionAsync(BaselineDefinition(_collectionName));

        (await _provisioner.CollectionExistsAsync(_collectionName)).Value.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCollectionAsync_RemovesTheCollection()
    {
        await _provisioner.EnsureCollectionAsync(BaselineDefinition(_collectionName));

        var deleteResult = await _provisioner.DeleteCollectionAsync(_collectionName);

        deleteResult.IsSuccess.Should().BeTrue();
        (await _provisioner.CollectionExistsAsync(_collectionName)).Value.Should().BeFalse();
    }
}
