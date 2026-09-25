using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemoryVectorCollection{TRecord}"/> against
/// <c>IVectorCollection&lt;TRecord&gt;</c>'s documented write/read/scroll contract -- no consuming
/// domain has adopted this fake yet, so this self-test is the only behavioral proof today, per the
/// SelfTests routing rule.
/// </summary>
public sealed class InMemoryVectorCollectionTests
{
    private const string ModelId = "test-model";

    [Fact]
    public void Constructor_NullDefinition_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemoryVectorCollection<TestVectorRecord>(null!));

    [Fact]
    public void CollectionName_ReturnsDefinitionName()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        Assert.Equal("chunks", collection.CollectionName);
    }

    // --- write: UpsertAsync ---

    [Fact]
    public async Task UpsertAsync_ValidRecord_Succeeds_AndIsRecorded()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(collection.CollectionName, result.Value.CollectionName);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ProviderToken));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Value.AcceptedAt);
        Assert.True(collection.WasUpserted("rec-1"));
        Assert.True(collection.IsQueryable("rec-1"));
        Assert.Contains("rec-1", collection.UpsertedIds);
    }

    [Fact]
    public async Task UpsertAsync_WhitespaceId_ReturnsInvalidRecordId_AndDoesNotStore()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.UpsertAsync(Rec("   ", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.InvalidRecordId("   "), result.Error);
        Assert.False(collection.IsQueryable("   "));
    }

    [Fact]
    public async Task UpsertAsync_ModelIdMismatch_ReturnsEmbeddingModelMismatch_AndDoesNotStore()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        var record = Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()) with { ModelId = "wrong-model" };

        var result = await collection.UpsertAsync(record, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.EmbeddingModelMismatch(collection.CollectionName, ModelId, "wrong-model"), result.Error);
        Assert.False(collection.IsQueryable("rec-1"));
    }

    [Fact]
    public async Task UpsertAsync_DimensionMismatch_ReturnsDimensionMismatch_AndDoesNotStore()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.UpsertAsync(Rec("rec-1", [1f, 0f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.DimensionMismatch(collection.CollectionName, 2, 3), result.Error);
        Assert.False(collection.IsQueryable("rec-1"));
    }

    [Fact]
    public async Task UpsertAsync_SimulateFailure_ReturnsFailure_AndDoesNotStore()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition()) { SimulateFailure = true };

        var result = await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(collection.IsQueryable("rec-1"));
    }

    [Fact]
    public async Task UpsertManyAsync_PartialInvalid_ReturnsSuccess_WithPerItemFailures()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        TestVectorRecord[] records =
        [
            Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()),
            Rec("   ", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()),
        ];

        var result = await collection.UpsertManyAsync(records, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SucceededCount);
        Assert.True(result.Value.HasFailures);
        Assert.Equal("   ", Assert.Single(result.Value.Failures).RecordId);
        Assert.True(collection.WasUpserted("rec-1"));
    }

    [Fact]
    public async Task UpsertManyAsync_SimulateFailure_ReturnsFailure()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition()) { SimulateFailure = true };

        var result = await collection.UpsertManyAsync([Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString())], TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // --- write: Delete ---

    [Fact]
    public async Task DeleteAsync_AbsentId_IsIdempotent_ReturnsSuccessWithZeroAffected()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.DeleteAsync("never-existed", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.AffectedCount);
        Assert.False(collection.WasDeleted("never-existed"));
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_Removes_AndRecordsDeletion()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var result = await collection.DeleteAsync("rec-1", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.True(collection.WasDeleted("rec-1"));
        Assert.False(collection.IsQueryable("rec-1"));
    }

    [Fact]
    public async Task DeleteAsync_SimulateFailure_ReturnsFailure_AndDoesNotDelete()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        collection.SimulateFailure = true;

        var result = await collection.DeleteAsync("rec-1", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(collection.IsQueryable("rec-1"));
    }

    [Fact]
    public async Task DeleteManyAsync_MixedPresence_CountsEveryRequestedIdAsSucceeded()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var result = await collection.DeleteManyAsync(["rec-1", "never-existed"], TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.SucceededCount);
        Assert.Empty(result.Value.Failures);
        Assert.True(collection.WasDeleted("rec-1"));
    }

    [Fact]
    public async Task DeleteByFilterAsync_TenantScopeMissingOnTenantedCollection_ReturnsFailure()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.DeleteByFilterAsync(VectorFilter.Eq("Status", "active"), TenantScope.Global, CancellationToken.None);

        Assert.Equal(IntelligenceErrors.TenantScopeMissing(collection.CollectionName), result.Error);
    }

    [Fact]
    public async Task DeleteByFilterAsync_InjectsOuterTenantAnd_OnlyDeletesMatchingTenantRecords()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-a", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString(), status: "active"), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        await collection.UpsertAsync(Rec("rec-b", [1f, 0f], tenantId: VectorTestTenants.TenantB.ToString(), status: "active"), TenantScope.For(VectorTestTenants.TenantB), CancellationToken.None);

        var result = await collection.DeleteByFilterAsync(VectorFilter.Eq("Status", "active"), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.True(collection.WasDeleted("rec-a"));
        Assert.False(collection.WasDeleted("rec-b"));
        Assert.True(collection.IsQueryable("rec-b"));
    }

    [Fact]
    public async Task DeleteByFilterAsync_SimulateFailure_ReturnsFailure()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition()) { SimulateFailure = true };

        var result = await collection.DeleteByFilterAsync(VectorFilter.Eq("Status", "active"), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // --- write: WaitUntilQueryableAsync ---

    [Fact]
    public async Task WaitUntilQueryableAsync_NullReceipt_Throws()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collection.WaitUntilQueryableAsync(null!, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public async Task WaitUntilQueryableAsync_AlwaysSucceeds_EveryFakeWriteIsImmediatelyQueryable()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        var receipt = (await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None)).Value;

        var result = await collection.WaitUntilQueryableAsync(receipt, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WaitUntilQueryableAsync_UnaffectedBySimulateFailure()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        var receipt = (await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None)).Value;
        collection.SimulateFailure = true;

        var result = await collection.WaitUntilQueryableAsync(receipt, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // --- read: QueryAsync fail-loud pipeline ---

    [Fact]
    public async Task QueryAsync_ModelIdMismatch_ReturnsFailure_NoIO()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var query = new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = "wrong-model" };
        var result = await collection.QueryAsync(query, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.EmbeddingModelMismatch(collection.CollectionName, ModelId, "wrong-model"), result.Error);
        Assert.Empty(collection.QueriedVectors);
    }

    [Fact]
    public async Task QueryAsync_DimensionMismatch_ReturnsFailure_NoIO()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var query = new VectorQuery { Vector = new[] { 1f, 0f, 0f }, ModelId = ModelId };
        var result = await collection.QueryAsync(query, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.DimensionMismatch(collection.CollectionName, 2, 3), result.Error);
        Assert.Empty(collection.QueriedVectors);
    }

    [Fact]
    public async Task QueryAsync_TenantScopeMissingOnTenantedCollection_ReturnsFailure_NoIO()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var query = new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId };
        var result = await collection.QueryAsync(query, TenantScope.Global, CancellationToken.None);

        Assert.Equal(IntelligenceErrors.TenantScopeMissing(collection.CollectionName), result.Error);
        Assert.Empty(collection.QueriedVectors);
    }

    [Fact]
    public async Task QueryAsync_ValidQuery_RecordsQueriedVectors()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var query = new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId };
        var result = await collection.QueryAsync(query, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(collection.QueriedVectors);
        Assert.Single(result.Value.Hits);
    }

    // --- read: filter AST coverage (via ScrollAsync, no vector needed) ---

    [Fact]
    public async Task ScrollAsync_Filter_EvaluatesAllEightAstNodeKinds()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition());
        await collection.UpsertManyAsync(
            [
                Rec("rec-1", [1f, 0f], status: "active", price: 10.0),
                Rec("rec-2", [1f, 0f], status: "retired", price: 20.0),
                Rec("rec-3", [1f, 0f], status: "active", price: 30.0),
            ],
            TenantScope.Global,
            CancellationToken.None);

        Assert.Equal(["rec-1", "rec-3"], await Ids(collection, VectorFilter.Eq("Status", "active")));
        Assert.Equal(["rec-2"], await Ids(collection, VectorFilter.Ne("Status", "active")));
        Assert.Equal(["rec-1", "rec-2", "rec-3"], await Ids(collection, VectorFilter.In("Status", "active", "retired")));
        Assert.Equal(["rec-1", "rec-2"], await Ids(collection, VectorFilter.Between("Price", 10.0, 20.0)));
        Assert.Equal(["rec-1", "rec-2", "rec-3"], await Ids(collection, VectorFilter.Exists("Status")));
        Assert.Equal(["rec-3"], await Ids(collection, VectorFilter.All(VectorFilter.Eq("Status", "active"), VectorFilter.Between("Price", 25.0, null))));
        Assert.Equal(["rec-2", "rec-3"], await Ids(collection, VectorFilter.Any(VectorFilter.Eq("Status", "retired"), VectorFilter.Eq("Price", 30.0))));
        Assert.Equal(["rec-2"], await Ids(collection, VectorFilter.Negate(VectorFilter.Eq("Status", "active"))));
    }

    private static async Task<string[]> Ids(InMemoryVectorCollection<TestVectorRecord> collection, VectorFilter filter)
    {
        var ids = new List<string>();
        await foreach (var record in collection.ScrollAsync(filter, TenantScope.Global, batchSize: 10, CancellationToken.None))
        {
            ids.Add(record.Id);
        }

        return [.. ids.OrderBy(id => id, StringComparer.Ordinal)];
    }

    [Fact]
    public async Task CountAsync_TenantScopeMissingOnTenantedCollection_ReturnsFailure()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.CountAsync(null, TenantScope.Global, CancellationToken.None);

        Assert.Equal(IntelligenceErrors.TenantScopeMissing(collection.CollectionName), result.Error);
    }

    [Fact]
    public async Task CountAsync_ExactCount_WithFilterAndTenantScope()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertManyAsync(
            [
                Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString(), status: "active"),
                Rec("rec-2", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString(), status: "retired"),
                Rec("rec-3", [1f, 0f], tenantId: VectorTestTenants.TenantB.ToString(), status: "active"),
            ],
            TenantScope.For(VectorTestTenants.TenantA),
            CancellationToken.None);

        var result = await collection.CountAsync(VectorFilter.Eq("Status", "active"), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }

    // --- GetAsync tenant handling ---

    [Fact]
    public async Task GetAsync_CorrectTenant_ReturnsRecord()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var result = await collection.GetAsync("rec-1", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("rec-1", result.Value.Id);
    }

    [Fact]
    public async Task GetAsync_WrongTenant_ReturnsRecordNotFound_NeverACrossTenantLeak()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var result = await collection.GetAsync("rec-1", TenantScope.For(VectorTestTenants.TenantB), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.RecordNotFound(collection.CollectionName, "rec-1"), result.Error);
    }

    [Fact]
    public async Task GetAsync_MissingId_ReturnsRecordNotFound()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        var result = await collection.GetAsync("never-existed", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(IntelligenceErrors.RecordNotFound(collection.CollectionName, "never-existed"), result.Error);
    }

    [Fact]
    public async Task GetAsync_NoTenantScopeOnTenantedCollection_HasNoUpfrontGuard_ReturnsRecordNotFound()
    {
        // GetAsync deliberately carries no upfront TenantScopeMissing check, unlike
        // Count/DeleteByFilter/Scroll -- a mismatch (including TenantScope.Global) folds into
        // RecordNotFound identically to a genuinely missing id.
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        var result = await collection.GetAsync("rec-1", TenantScope.Global, CancellationToken.None);

        Assert.Equal(IntelligenceErrors.RecordNotFound(collection.CollectionName, "rec-1"), result.Error);
    }

    // --- ScrollAsync tenant/cancellation ---

    [Fact]
    public async Task ScrollAsync_TenantScopeMissingOnTenantedCollection_ThrowsIntelligenceStreamException()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        await Assert.ThrowsAsync<IntelligenceStreamException>(async () =>
        {
            await foreach (var _ in collection.ScrollAsync(null, TenantScope.Global, batchSize: 10, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task ScrollAsync_YieldsFullFilteredTenantScopedCorpus()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertManyAsync(
            [
                Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()),
                Rec("rec-2", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()),
                Rec("rec-3", [1f, 0f], tenantId: VectorTestTenants.TenantB.ToString()),
            ],
            TenantScope.For(VectorTestTenants.TenantA),
            CancellationToken.None);

        var ids = new List<string>();
        await foreach (var record in collection.ScrollAsync(null, TenantScope.For(VectorTestTenants.TenantA), batchSize: 10, CancellationToken.None))
        {
            ids.Add(record.Id);
        }

        Assert.Equal(["rec-1", "rec-2"], ids.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScrollAsync_CancellationMidEnumeration_ThrowsOperationCanceledException()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        for (var i = 0; i < 5; i++)
        {
            await collection.UpsertAsync(Rec($"rec-{i}", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var record in collection.ScrollAsync(null, TenantScope.For(VectorTestTenants.TenantA), batchSize: 10, cts.Token))
            {
                seen.Add(record.Id);
                if (seen.Count == 1)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(seen.Count < 5);
    }

    // --- Score/Rank directionality per DistanceMetric ---

    [Fact]
    public async Task QueryAsync_CosineMetric_RanksDescending_HigherSimilarityFirst()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition(VectorDistanceMetric.Cosine));
        await collection.UpsertAsync(Rec("y", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        await collection.UpsertAsync(Rec("z", [2f, 2f]), TenantScope.Global, CancellationToken.None);

        var result = await collection.QueryAsync(new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId }, TenantScope.Global, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["y", "z"], result.Value.Hits.Select(h => h.Record.Id));
        Assert.Equal(0, result.Value.Hits[0].Rank);
        Assert.Equal(1, result.Value.Hits[1].Rank);
        Assert.True(result.Value.Hits[0].Score > result.Value.Hits[1].Score);
        Assert.Equal(1.0, result.Value.Hits[0].Score, precision: 3);
    }

    [Fact]
    public async Task QueryAsync_DotProductMetric_RanksDescending_ByMagnitudeSensitiveScore()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition(VectorDistanceMetric.DotProduct));
        await collection.UpsertAsync(Rec("y", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        await collection.UpsertAsync(Rec("z", [2f, 2f]), TenantScope.Global, CancellationToken.None);

        var result = await collection.QueryAsync(new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId }, TenantScope.Global, CancellationToken.None);

        // Dot product is magnitude-sensitive -- z (dot=2) ranks ABOVE y (dot=1), the opposite order
        // from cosine similarity for this same pair, proving the two metrics are not interchangeable.
        Assert.True(result.IsSuccess);
        Assert.Equal(["z", "y"], result.Value.Hits.Select(h => h.Record.Id));
        Assert.Equal(2.0, result.Value.Hits[0].Score, precision: 3);
        Assert.Equal(1.0, result.Value.Hits[1].Score, precision: 3);
    }

    [Fact]
    public async Task QueryAsync_EuclideanMetric_RanksAscending_SmallerDistanceFirst()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition(VectorDistanceMetric.Euclidean));
        await collection.UpsertAsync(Rec("y", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        await collection.UpsertAsync(Rec("z", [2f, 2f]), TenantScope.Global, CancellationToken.None);

        var result = await collection.QueryAsync(new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId }, TenantScope.Global, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["y", "z"], result.Value.Hits.Select(h => h.Record.Id));
        Assert.Equal(0.0, result.Value.Hits[0].Score, precision: 3);
        Assert.True(result.Value.Hits[1].Score > result.Value.Hits[0].Score);
    }

    [Fact]
    public async Task QueryAsync_MinScore_Cosine_FiltersBelowThreshold()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition(VectorDistanceMetric.Cosine));
        await collection.UpsertAsync(Rec("y", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        await collection.UpsertAsync(Rec("z", [2f, 2f]), TenantScope.Global, CancellationToken.None);

        var result = await collection.QueryAsync(
            new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId, MinScore = 0.9f },
            TenantScope.Global,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["y"], result.Value.Hits.Select(h => h.Record.Id));
    }

    [Fact]
    public async Task QueryAsync_MinScore_Euclidean_FiltersAboveThreshold()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition(VectorDistanceMetric.Euclidean));
        await collection.UpsertAsync(Rec("y", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        await collection.UpsertAsync(Rec("z", [2f, 2f]), TenantScope.Global, CancellationToken.None);

        var result = await collection.QueryAsync(
            new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId, MinScore = 1.0f },
            TenantScope.Global,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["y"], result.Value.Hits.Select(h => h.Record.Id));
    }

    [Fact]
    public async Task QueryAsync_Limit_CapsHitCount()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(GlobalDefinition());
        for (var i = 0; i < 5; i++)
        {
            await collection.UpsertAsync(Rec($"rec-{i}", [1f, 0f]), TenantScope.Global, CancellationToken.None);
        }

        var result = await collection.QueryAsync(
            new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId, Limit = 2 },
            TenantScope.Global,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Hits.Count);
    }

    // --- SimulateFailure scope, Seed, Reset ---

    [Fact]
    public async Task SimulateFailure_OnlyAffectsWritePath_ReadPathAndWaitUntilQueryableUnaffected()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        var upsertResult = await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        collection.SimulateFailure = true;

        var query = new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId };
        Assert.True((await collection.QueryAsync(query, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await collection.GetAsync("rec-1", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await collection.CountAsync(null, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await collection.WaitUntilQueryableAsync(upsertResult.Value, TimeSpan.FromSeconds(1), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public void Seed_PopulatesStore_WithoutGoingThroughUpsertAsync()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());

        collection.Seed(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()));

        Assert.True(collection.IsQueryable("rec-1"));
        Assert.False(collection.WasUpserted("rec-1"));
    }

    [Fact]
    public void Seed_WhitespaceId_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() =>
            new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition()).Seed(Rec("   ", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString())));

    [Fact]
    public async Task Reset_ClearsStoreHistoryAndQueriedVectors()
    {
        var collection = new InMemoryVectorCollection<TestVectorRecord>(TenantedDefinition());
        await collection.UpsertAsync(Rec("rec-1", [1f, 0f], tenantId: VectorTestTenants.TenantA.ToString()), TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        await collection.DeleteAsync("rec-1", TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);
        await collection.QueryAsync(new VectorQuery { Vector = new[] { 1f, 0f }, ModelId = ModelId }, TenantScope.For(VectorTestTenants.TenantA), CancellationToken.None);

        collection.Reset();

        Assert.False(collection.WasUpserted("rec-1"));
        Assert.False(collection.WasDeleted("rec-1"));
        Assert.False(collection.IsQueryable("rec-1"));
        Assert.Empty(collection.QueriedVectors);
    }

    private static TestVectorRecord Rec(
        string id,
        float[] vector,
        string modelId = ModelId,
        string? tenantId = null,
        string? status = null,
        double? price = null)
    {
        var metadata = new Dictionary<string, VectorValue>();
        if (tenantId is not null)
        {
            metadata["TenantId"] = VectorValue.From(tenantId);
        }

        if (status is not null)
        {
            metadata["Status"] = VectorValue.From(status);
        }

        if (price is not null)
        {
            metadata["Price"] = VectorValue.From(price.Value);
        }

        return new TestVectorRecord
        {
            Id = id,
            Vector = vector,
            ModelId = modelId,
            Metadata = metadata,
        };
    }

    private static VectorCollectionDefinition TenantedDefinition() =>
        new VectorCollectionDefinitionBuilder("chunks")
            .EmbeddingModel(ModelId, 2)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .TenantField("TenantId")
            .Field("TenantId", VectorFieldKind.String, filterable: true)
            .Field("Status", VectorFieldKind.String, filterable: true)
            .Field("Price", VectorFieldKind.Double, filterable: true)
            .Build()
            .Value;

    private static VectorCollectionDefinition GlobalDefinition(VectorDistanceMetric metric = VectorDistanceMetric.Cosine) =>
        new VectorCollectionDefinitionBuilder("chunks-global")
            .EmbeddingModel(ModelId, 2)
            .DistanceMetric(metric)
            .Field("Status", VectorFieldKind.String, filterable: true)
            .Field("Price", VectorFieldKind.Double, filterable: true)
            .Build()
            .Value;
}
