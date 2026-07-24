using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.AI.Qdrant.Tests.Conformance;

/// <summary>
/// Real-backend behavioural conformance suite (T-03) against a genuine <c>Testcontainers.Qdrant</c>-backed
/// engine — round-trip upsert/query/get/count/scroll, filter translation for all eight
/// <see cref="VectorFilter"/> nodes, tenant-scope outermost-conjunction proof (on both reads and a
/// filtered delete), and the <see cref="QdrantVectorCollection{TRecord}.WaitUntilQueryableAsync"/> barrier.
/// Never mocked — behavioural correctness is only observable against the real engine.
/// </summary>
/// <remarks>
/// Record ids throughout this suite are unsigned-integer strings (never mnemonic labels like
/// <c>"a1"</c>) — Qdrant point ids accept only an unsigned 64-bit integer or a canonical-form UUID
/// natively (<see cref="SharedKernel.AI.Qdrant.Collections.QdrantRecordMapper.ToPointId"/>), so a
/// non-conforming literal id is rejected as <c>intelligence.invalid_record_id</c> before any I/O. The
/// <c>IdA1</c>/<c>IdA2</c>/... constants below preserve the original mnemonic grouping (the "A" corpus
/// vs. the "B" corpus) while staying legal on the wire.
/// </remarks>
[Collection(QdrantConformanceCollection.Name)]
public sealed class QdrantVectorCollectionConformanceTests : IAsyncLifetime
{
    private const string ModelId = "conformance-model";
    private const int Dimension = 4;

    private const string IdA1 = "101";
    private const string IdA2 = "102";
    private const string IdA3 = "103";
    private const string IdB1 = "201";
    private const string IdB2 = "202";

    private static readonly TenantScope TenantA = TenantScope.Of("tenant-a");
    private static readonly TenantScope TenantB = TenantScope.Of("tenant-b");

    private readonly QdrantContainerFixture _fixture;
    private readonly string _collectionName = $"conformance-corpus-{Guid.NewGuid():N}";

    private QdrantClient _client = null!;
    private QdrantCollectionProvisioner _provisioner = null!;
    private QdrantVectorCollection<TestVectorRecord> _collection = null!;

    public QdrantVectorCollectionConformanceTests(QdrantContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var grpcUri = new Uri(_fixture.GrpcEndpoint);
        _client = new QdrantClient(grpcUri.Host, grpcUri.Port);

        var definitionResult = new VectorCollectionDefinitionBuilder(_collectionName)
            .EmbeddingModel(ModelId, Dimension)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .TenantField("tenantId")
            .Field("tenantId", VectorFieldKind.String, filterable: true)
            .Field("category", VectorFieldKind.String, filterable: true)
            .Field("priority", VectorFieldKind.Int64, filterable: true)
            .Field("score", VectorFieldKind.Double, filterable: true)
            .Field("tags", VectorFieldKind.String, filterable: true)
            .Build();

        var definition = definitionResult.Value;

        _provisioner = new QdrantCollectionProvisioner(_client, NullLogger<QdrantCollectionProvisioner>.Instance);
        var ensureResult = await _provisioner.EnsureCollectionAsync(definition);
        if (ensureResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to provision the conformance collection: {ensureResult.Error.Message}");
        }

        _collection = new QdrantVectorCollection<TestVectorRecord>(
            _client, definition, new FakeClock(), NullLogger<QdrantVectorCollection<TestVectorRecord>>.Instance);

        foreach (var (record, tenant) in Corpus())
        {
            var upsertResult = await _collection.UpsertAsync(record, tenant);
            if (upsertResult.IsFailure)
            {
                throw new InvalidOperationException($"Failed to seed record '{record.Id}': {upsertResult.Error.Message}");
            }
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _provisioner.DeleteCollectionAsync(_collectionName);
        }
        catch
        {
            // Best-effort — the container itself is torn down at the end of the test run regardless.
        }
    }

    // -----------------------------------------------------------------------------------------
    // corpus
    // -----------------------------------------------------------------------------------------

    private static IEnumerable<(TestVectorRecord Record, TenantScope Tenant)> Corpus()
    {
        yield return (Record(IdA1, [1, 0, 0, 0], "fiction", 1, 0.9, tags: "scifi"), TenantA);
        yield return (Record(IdA2, [0, 1, 0, 0], "nonfiction", 2, 0.5), TenantA);
        yield return (Record(IdA3, [0, 0, 1, 0], "fiction", 3, 0.75), TenantA);
        yield return (Record(IdB1, [1, 0, 0, 0], "fiction", 1, 0.2), TenantB);
        yield return (Record(IdB2, [0, 0, 0, 1], "mystery", 5, 0.95), TenantB);
    }

    private static TestVectorRecord Record(string id, float[] vector, string category, long priority, double score, string? tags = null)
    {
        var metadata = new Dictionary<string, VectorValue>(StringComparer.Ordinal)
        {
            ["category"] = VectorValue.From(category),
            ["priority"] = VectorValue.From(priority),
            ["score"] = VectorValue.From(score),
        };

        if (tags is not null)
        {
            metadata["tags"] = VectorValue.From(tags);
        }

        return new TestVectorRecord { Id = id, Vector = vector, ModelId = ModelId, Metadata = metadata };
    }

    // -----------------------------------------------------------------------------------------
    // round-trip: get / count / scroll
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_RoundTripsUpsertedRecord_WithMetadataAndVector()
    {
        var result = await _collection.GetAsync(IdA1, TenantA);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(IdA1);
        result.Value.ModelId.Should().Be(ModelId);
        result.Value.Vector.ToArray().Should().BeEquivalentTo(new float[] { 1, 0, 0, 0 });
        result.Value.Metadata["category"].AsString.Should().Be("fiction");
        result.Value.Metadata["priority"].AsInt64.Should().Be(1);
        result.Value.Metadata["score"].AsDouble.Should().Be(0.9);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsRecordNotFound()
    {
        // A well-formed (numeric) but never-seeded id — proves the RecordNotFound path specifically,
        // distinct from InvalidRecordId (which a mnemonic literal like "does-not-exist" would trigger
        // instead, since it fails the ulong/UUID charset check before any I/O is even attempted).
        var result = await _collection.GetAsync("999999", TenantA);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.record_not_found");
    }

    [Fact]
    public async Task GetAsync_WrongTenantScope_ReturnsRecordNotFound_ProvingIsolation()
    {
        var result = await _collection.GetAsync(IdA1, TenantB);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("intelligence.record_not_found");
    }

    [Fact]
    public async Task CountAsync_IsExact_PerTenant()
    {
        var tenantACount = await _collection.CountAsync(null, TenantA);
        var tenantBCount = await _collection.CountAsync(null, TenantB);

        tenantACount.IsSuccess.Should().BeTrue();
        tenantACount.Value.Should().Be(3);
        tenantBCount.Value.Should().Be(2);
    }

    [Fact]
    public async Task ScrollAsync_YieldsFullTenantCorpus_ScopedToTenant()
    {
        var ids = new List<string>();
        await foreach (var record in _collection.ScrollAsync(null, TenantA, batchSize: 2))
        {
            ids.Add(record.Id);
        }

        ids.Should().BeEquivalentTo([IdA1, IdA2, IdA3]);
    }

    [Fact]
    public async Task ScrollAsync_CancellationMidEnumeration_StopsFurtherYields()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<string>();
        Exception? caught = null;

        try
        {
            await foreach (var record in _collection.ScrollAsync(null, TenantA, batchSize: 1, cts.Token))
            {
                seen.Add(record.Id);
                cts.Cancel();
            }
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull("cancelling mid-enumeration must stop the stream rather than silently completing it");
        seen.Should().HaveCount(1, "no further records may be yielded once cancellation is requested");
    }

    // -----------------------------------------------------------------------------------------
    // similarity query + tenant isolation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_ScopesResultsToTenant_ExcludingAnIdenticalVectorOwnedByAnotherTenant()
    {
        // IdB1 shares the identical vector [1,0,0,0] with IdA1 (a perfect match), but querying under
        // TenantA must never surface tenant B's record regardless of similarity.
        var query = new VectorQuery { Vector = new float[] { 1, 0, 0, 0 }, ModelId = ModelId, Limit = 10 };

        var result = await _collection.QueryAsync(query, TenantA);

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value.Hits.Select(h => h.Record.Id).ToList();
        ids.Should().Contain(IdA1);
        ids.Should().NotContain(IdB1);
    }

    [Fact]
    public async Task QueryAsync_ReturnVectorDefaultsFalse_OmittingVectorsWithoutFaulting()
    {
        // VectorQuery.ReturnVector defaults false, and Qdrant genuinely omits the Vectors submessage
        // (a null reference on the wire, not an empty default instance) when vectors were not
        // requested — this is the regression proof for a real NullReferenceException this conformance
        // suite caught: every query in this file defaults ReturnVector, so their mere success is
        // already an implicit proof, but this test makes the specific behaviour explicit.
        var query = new VectorQuery { Vector = new float[] { 1, 0, 0, 0 }, ModelId = ModelId, Limit = 1 };

        var result = await _collection.QueryAsync(query, TenantA);

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().NotBeEmpty();
        result.Value.Hits[0].Record.Vector.Length.Should().Be(0, "ReturnVector was never opted into");
    }

    [Fact]
    public async Task QueryAsync_ReturnVectorTrue_PopulatesTheVector()
    {
        var query = new VectorQuery { Vector = new float[] { 1, 0, 0, 0 }, ModelId = ModelId, Limit = 1, ReturnVector = true };

        var result = await _collection.QueryAsync(query, TenantA);

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().NotBeEmpty();
        result.Value.Hits[0].Record.Vector.ToArray().Should().BeEquivalentTo(new float[] { 1, 0, 0, 0 });
    }

    // -----------------------------------------------------------------------------------------
    // filter translation — all eight VectorFilter AST nodes
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_EqualFilter_MatchesExactValue()
    {
        var hits = await QueryWithFilter(VectorFilter.Eq("category", VectorValue.From("fiction")));

        hits.Should().BeEquivalentTo([IdA1, IdA3]);
    }

    [Fact]
    public async Task QueryAsync_NotEqualFilter_ExcludesMatchingValue()
    {
        var hits = await QueryWithFilter(VectorFilter.Ne("category", VectorValue.From("fiction")));

        hits.Should().BeEquivalentTo([IdA2]);
    }

    [Fact]
    public async Task QueryAsync_InFilter_MatchesAnyListedValue()
    {
        var hits = await QueryWithFilter(VectorFilter.In("category", VectorValue.From("fiction"), VectorValue.From("nonfiction")));

        hits.Should().BeEquivalentTo([IdA1, IdA2, IdA3]);
    }

    [Fact]
    public async Task QueryAsync_RangeFilter_MatchesInclusiveBounds()
    {
        var hits = await QueryWithFilter(VectorFilter.Between("priority", VectorValue.From(2L), VectorValue.From(3L)));

        hits.Should().BeEquivalentTo([IdA2, IdA3]);
    }

    [Fact]
    public async Task QueryAsync_ExistsFilter_MatchesOnlyRecordsWithTheField()
    {
        var hits = await QueryWithFilter(VectorFilter.Exists("tags"));

        hits.Should().BeEquivalentTo([IdA1]);
    }

    [Fact]
    public async Task QueryAsync_AndFilter_RequiresAllOperandsToMatch()
    {
        var hits = await QueryWithFilter(VectorFilter.All(
            VectorFilter.Eq("category", VectorValue.From("fiction")),
            VectorFilter.Between("priority", VectorValue.From(3L), null)));

        hits.Should().BeEquivalentTo([IdA3]);
    }

    [Fact]
    public async Task QueryAsync_OrFilter_MatchesAnyOperand()
    {
        var hits = await QueryWithFilter(VectorFilter.Any(
            VectorFilter.Eq("category", VectorValue.From("nonfiction")),
            VectorFilter.Eq("priority", VectorValue.From(1L))));

        hits.Should().BeEquivalentTo([IdA1, IdA2]);
    }

    [Fact]
    public async Task QueryAsync_NotFilter_NegatesOperand()
    {
        var hits = await QueryWithFilter(VectorFilter.Negate(VectorFilter.Eq("category", VectorValue.From("fiction"))));

        hits.Should().BeEquivalentTo([IdA2]);
    }

    private async Task<List<string>> QueryWithFilter(VectorFilter filter)
    {
        var query = new VectorQuery { Vector = new float[] { 1, 0, 0, 0 }, ModelId = ModelId, Filter = filter, Limit = 10 };
        var result = await _collection.QueryAsync(query, TenantA);
        result.IsSuccess.Should().BeTrue();
        return result.Value.Hits.Select(h => h.Record.Id).ToList();
    }

    // -----------------------------------------------------------------------------------------
    // write surface: upsert-many, delete (single / many / by-filter), WaitUntilQueryableAsync
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task UpsertManyAsync_BulkWritesAllRecords_ReportedInTheReceipt()
    {
        const string idX = "301";
        const string idY = "302";
        var recordX = Record(idX, [0.1f, 0, 0, 0], "fiction", 1, 0.1);
        var recordY = Record(idY, [0, 0.1f, 0, 0], "fiction", 1, 0.1);

        var result = await _collection.UpsertManyAsync([recordX, recordY], TenantA);

        result.IsSuccess.Should().BeTrue();
        result.Value.SucceededCount.Should().Be(2);
        result.Value.HasFailures.Should().BeFalse();

        await _collection.DeleteManyAsync([idX, idY], TenantA);
    }

    [Fact]
    public async Task WaitUntilQueryableAsync_AfterUpsert_Succeeds_AndTheRecordIsImmediatelyReadable()
    {
        const string id = "401";
        var record = Record(id, [0.5f, 0.5f, 0, 0], "fiction", 9, 0.1);
        var upsertResult = await _collection.UpsertAsync(record, TenantA);
        upsertResult.IsSuccess.Should().BeTrue();

        var waitResult = await _collection.WaitUntilQueryableAsync(upsertResult.Value, TimeSpan.FromSeconds(5));

        waitResult.IsSuccess.Should().BeTrue();
        (await _collection.GetAsync(id, TenantA)).IsSuccess.Should().BeTrue();

        await _collection.DeleteAsync(id, TenantA);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord_TenantScoped()
    {
        const string id = "501";
        var record = Record(id, [0.2f, 0.2f, 0.2f, 0.2f], "fiction", 1, 0.1);
        (await _collection.UpsertAsync(record, TenantA)).IsSuccess.Should().BeTrue();

        var deleteResult = await _collection.DeleteAsync(id, TenantA);

        deleteResult.IsSuccess.Should().BeTrue();
        (await _collection.GetAsync(id, TenantA)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesAllRequestedRecords()
    {
        const string idX = "601";
        const string idY = "602";
        var recordX = Record(idX, [0.1f, 0, 0, 0], "fiction", 1, 0.1);
        var recordY = Record(idY, [0, 0.1f, 0, 0], "fiction", 1, 0.1);
        (await _collection.UpsertManyAsync([recordX, recordY], TenantA)).IsSuccess.Should().BeTrue();

        var deleteResult = await _collection.DeleteManyAsync([idX, idY], TenantA);

        deleteResult.IsSuccess.Should().BeTrue();
        deleteResult.Value.SucceededCount.Should().Be(2);
        (await _collection.GetAsync(idX, TenantA)).IsFailure.Should().BeTrue();
        (await _collection.GetAsync(idY, TenantA)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteByFilterAsync_RemovesOnlyMatchingRecords_WithTenantAsTheOutermostConjunction()
    {
        const string idA = "701";
        const string idB = "702";
        var targetRecord = Record(idA, [0, 0, 0, 0.1f], "temp-category", 1, 0.1);
        (await _collection.UpsertAsync(targetRecord, TenantA)).IsSuccess.Should().BeTrue();

        var sameFilterOtherTenant = Record(idB, [0, 0, 0, 0.1f], "temp-category", 1, 0.1);
        (await _collection.UpsertAsync(sameFilterOtherTenant, TenantB)).IsSuccess.Should().BeTrue();

        var deleteResult = await _collection.DeleteByFilterAsync(VectorFilter.Eq("category", VectorValue.From("temp-category")), TenantA);

        deleteResult.IsSuccess.Should().BeTrue();
        (await _collection.GetAsync(idA, TenantA)).IsFailure.Should().BeTrue();

        // The tenant-B record matching the SAME filter must never be touched by a tenant-A-scoped
        // delete — the outermost-conjunction tenant guard applies to filtered writes too, not just reads.
        (await _collection.GetAsync(idB, TenantB)).IsSuccess.Should().BeTrue();

        await _collection.DeleteAsync(idB, TenantB);
    }
}
