using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.AI.Qdrant.Tests.Collections;

/// <summary>
/// Fail-loud, no-I/O proof tests. <see cref="IQdrantClient"/> is a real interface, so an unconfigured
/// <c>NSubstitute</c> substitute (never a live connection) both proves "no call was attempted" via
/// <c>ReceivedCalls()</c> and, in the companion tests, proves the validation guard is what stopped I/O
/// by showing the same call path throws once the guard is satisfied (NSubstitute auto-completes an
/// unconfigured <c>Task&lt;T&gt;</c>-returning member with a default-valued result, so dereferencing
/// that null reference-typed result surfaces as <see cref="NullReferenceException"/>).
/// </summary>
public sealed class QdrantVectorCollectionNoIoTests
{
    private static VectorCollectionDefinition TenantedDefinition() => new VectorCollectionDefinitionBuilder("products")
        .EmbeddingModel("model-a", 2)
        .DistanceMetric(VectorDistanceMetric.Cosine)
        .TenantField("tenantId")
        .Field("tenantId", VectorFieldKind.String, filterable: true)
        .Build()
        .Value;

    private static VectorCollectionDefinition UntenantedDefinition() => VectorCollectionDefinition
        .Create("products", "model-a", 2, VectorDistanceMetric.Cosine, [])
        .Value;

    private static QdrantVectorCollection<TestVectorRecord> CreateCollection(IQdrantClient client, VectorCollectionDefinition definition) =>
        new(client, definition, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);

    [Fact]
    public async Task UpsertAsync_TenantScopeMissing_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, TenantedDefinition());
        var record = new TestVectorRecord { Id = "1", ModelId = "model-a", Vector = new float[] { 0.1f, 0.2f } };

        var result = await collection.UpsertAsync(record, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.tenant_scope_missing");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_EmbeddingModelMismatch_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var record = new TestVectorRecord { Id = "1", ModelId = "wrong-model", Vector = new float[] { 0.1f, 0.2f } };

        var result = await collection.UpsertAsync(record, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.embedding_model_mismatch");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_DimensionMismatch_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var record = new TestVectorRecord { Id = "1", ModelId = "model-a", Vector = new float[] { 0.1f, 0.2f, 0.3f } };

        var result = await collection.UpsertAsync(record, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.dimension_mismatch");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_ValidatedInputs_ReachesClient_ProvingTheGuardStoppedTheRejectedCases()
    {
        // Companion to the three rejection tests above: proves the guard itself — not an unrelated
        // short-circuit — is what stopped I/O in those cases, by showing the identical call path
        // genuinely reaches the client once every validation passes.
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var record = new TestVectorRecord { Id = "1", ModelId = "model-a", Vector = new float[] { 0.1f, 0.2f } };

        await collection.UpsertAsync(record, TenantScope.None);

        client.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAsync_TenantScopeMissing_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, TenantedDefinition());

        var result = await collection.GetAsync("1", TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.tenant_scope_missing");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_InvalidRecordId_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());

        var result = await collection.GetAsync("not a valid qdrant id!!", TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.invalid_record_id");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_EmbeddingModelMismatch_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f }, ModelId = "wrong-model" };

        var result = await collection.QueryAsync(query, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.embedding_model_mismatch");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_DimensionMismatch_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var query = new VectorQuery { Vector = new float[] { 0.1f, 0.2f, 0.3f }, ModelId = "model-a" };

        var result = await collection.QueryAsync(query, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.dimension_mismatch");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CountAsync_TenantScopeMissing_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, TenantedDefinition());

        var result = await collection.CountAsync(null, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.tenant_scope_missing");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteByFilterAsync_TenantScopeMissing_ReturnsFailure_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, TenantedDefinition());

        var result = await collection.DeleteByFilterAsync(VectorFilter.Exists("tags"), TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.tenant_scope_missing");
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ScrollAsync_TenantScopeMissing_ThrowsIntelligenceStreamException_BeforeYieldingAnything()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, TenantedDefinition());

        var act = async () =>
        {
            await foreach (var _ in collection.ScrollAsync(null, TenantScope.None, batchSize: 10))
            {
            }
        };

        await act.Should().ThrowAsync<IntelligenceStreamException>();
        client.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task WaitUntilQueryableAsync_AlwaysSucceeds_WithNoIo()
    {
        var client = Substitute.For<IQdrantClient>();
        var collection = CreateCollection(client, UntenantedDefinition());
        var receipt = new VectorWriteReceipt
        {
            CollectionName = "products",
            ProviderToken = "1",
            AffectedCount = 1,
            AcceptedAt = DateTimeOffset.UtcNow,
        };

        var result = await collection.WaitUntilQueryableAsync(receipt, TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        client.ReceivedCalls().Should().BeEmpty();
    }
}
