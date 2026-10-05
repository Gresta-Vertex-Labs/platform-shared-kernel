using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.AI.Qdrant.Tests.Conformance;

/// <summary>
/// Real-backend proof (T-03) of <see cref="QdrantCollectionProvisioner.CutoverAsync"/>'s staging→live
/// alias swap — including a second cutover proving the previously-live staging collection is deleted
/// once <see cref="VectorCollectionCutoverRequest.DeleteStagingAfterCutover"/> is honored.
/// </summary>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantCutoverConformanceTests : IAsyncLifetime
{
    private const string ModelId = "cutover-model";

    private readonly QdrantContainerFixture _fixture;
    private readonly string _stagingV1 = $"cutover-staging-v1-{Guid.NewGuid():N}";
    private readonly string _stagingV2 = $"cutover-staging-v2-{Guid.NewGuid():N}";
    private readonly string _liveAlias = $"cutover-live-{Guid.NewGuid():N}";

    private QdrantClient _client = null!;
    private QdrantCollectionProvisioner _provisioner = null!;

    public QdrantCutoverConformanceTests(QdrantContainerFixture fixture)
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
            await _provisioner.DeleteCollectionAsync(_stagingV1);
        }
        catch
        {
            // Best-effort — v1 is deliberately deleted by the second cutover in the test itself.
        }

        try
        {
            await _provisioner.DeleteCollectionAsync(_stagingV2);
        }
        catch
        {
            // Best-effort — the container itself is torn down at the end of the test run regardless.
        }
    }

    [Fact]
    public async Task CutoverAsync_SwapsAliasFromStagingToLive_AndASecondCutoverDeletesTheOrphanedPredecessor()
    {
        var definitionV1 = new VectorCollectionDefinitionBuilder(_stagingV1)
            .EmbeddingModel(ModelId, 2)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build()
            .Value;
        (await _provisioner.EnsureCollectionAsync(definitionV1)).IsSuccess.Should().BeTrue();

        var collectionV1 = new QdrantVectorCollection<TestVectorRecord>(
            _client, definitionV1, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);
        var recordV1 = new TestVectorRecord { Id = "1", Vector = new float[] { 1, 0 }, ModelId = ModelId };
        (await collectionV1.UpsertAsync(recordV1, TenantScope.Global)).IsSuccess.Should().BeTrue();

        // First cutover: staging v1 -> live alias, retaining the staging collection.
        var firstCutover = await _provisioner.CutoverAsync(new VectorCollectionCutoverRequest
        {
            StagingCollectionName = _stagingV1,
            LiveCollectionName = _liveAlias,
            DeleteStagingAfterCutover = false,
        });
        firstCutover.IsSuccess.Should().BeTrue();

        var liveDefinition = definitionV1 with { Name = _liveAlias };
        var liveCollection = new QdrantVectorCollection<TestVectorRecord>(
            _client, liveDefinition, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);

        (await liveCollection.GetAsync("1", TenantScope.Global)).IsSuccess.Should().BeTrue(
            "the alias must now resolve reads through to the staging v1 collection");
        (await _provisioner.CollectionExistsAsync(_stagingV1)).Value.Should().BeTrue(
            "DeleteStagingAfterCutover was false — the staging collection must still exist");

        // Second cutover: a freshly re-embedded staging v2 -> live alias, deleting the orphaned v1.
        var definitionV2 = new VectorCollectionDefinitionBuilder(_stagingV2)
            .EmbeddingModel(ModelId, 2)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build()
            .Value;
        (await _provisioner.EnsureCollectionAsync(definitionV2)).IsSuccess.Should().BeTrue();

        var collectionV2 = new QdrantVectorCollection<TestVectorRecord>(
            _client, definitionV2, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);
        var recordV2 = new TestVectorRecord { Id = "2", Vector = new float[] { 0, 1 }, ModelId = ModelId };
        (await collectionV2.UpsertAsync(recordV2, TenantScope.Global)).IsSuccess.Should().BeTrue();

        var secondCutover = await _provisioner.CutoverAsync(new VectorCollectionCutoverRequest
        {
            StagingCollectionName = _stagingV2,
            LiveCollectionName = _liveAlias,
            DeleteStagingAfterCutover = true,
        });
        secondCutover.IsSuccess.Should().BeTrue();

        (await liveCollection.GetAsync("2", TenantScope.Global)).IsSuccess.Should().BeTrue(
            "the alias must now resolve reads through to the staging v2 collection");
        (await liveCollection.GetAsync("1", TenantScope.Global)).IsFailure.Should().BeTrue(
            "the live alias now points at v2, which never had v1's record");
        (await _provisioner.CollectionExistsAsync(_stagingV1)).Value.Should().BeFalse(
            "the orphaned v1 collection must be deleted once DeleteStagingAfterCutover is honored on the second cutover");
    }
}
