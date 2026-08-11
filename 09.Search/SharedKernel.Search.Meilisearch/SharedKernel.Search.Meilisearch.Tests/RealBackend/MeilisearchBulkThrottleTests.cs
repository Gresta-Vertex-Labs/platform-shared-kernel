using System.Diagnostics;
using System.Text.Json.Serialization;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-33/T-34 (P-354, real-backend): against a real Meilisearch container — the pacing proof mirroring
/// ElasticSearch's T-32 one-for-one, the C-54 regression proof (the restructured per-batch dispatch
/// loop — single-document <c>AddDocumentsAsync</c> calls in an explicit loop, replacing the SDK's own
/// <c>AddDocumentsInBatchesAsync</c> — still produces a correct, partially-failed
/// <see cref="SearchBulkReceipt"/> for a deliberately-partially-invalid batch, exactly the
/// per-batch-task-failure shape the pre-restructuring implementation also had to handle), and T-34's
/// re-verification that per-document failure reporting is unaffected by the throttle itself: a
/// deliberately-invalid document inside a THROTTLED, multi-tenant, multi-batch call still surfaces as
/// its own <c>SearchItemFailure</c>, never silently dropped or swallowed by the new pacing logic, and
/// its differently-tenanted siblings are never dropped either.
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchBulkThrottleTests : IAsyncLifetime
{
    private const string IndexName = "products-bulk-throttle-tests";
    private const string TenantId = "throttle-tenant";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchBulkThrottleTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task IndexManyAsync_WithMaxBatchesPerSecondConfigured_MeasurablyPacesBatchDispatch_WhileUnconfiguredCallStaysFast()
    {
        var options = MeilisearchProviderFactory.CreateOptions(_fixture);
        options.DefaultBatchSize = 2; // 6 documents => 3 dispatch batches (2 + 2 + 2)
        var client = MeilisearchProviderFactory.CreateClient(_fixture);
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(client, _definition, options);

        var throttledBatch = Enumerable.Range(1, 6).Select(i => NewProduct($"throttled-{i}")).ToArray();
        // 4 batches/sec => a 250ms delay before each non-first batch; two delays => >= ~500ms of pacing.
        var bulkOptions = new SearchBulkWriteOptions { MaxBatchesPerSecond = 4 };

        var throttledStopwatch = Stopwatch.StartNew();
        var throttledResult = await index.IndexManyAsync(throttledBatch, SearchWriteConsistency.Accepted, bulkOptions);
        throttledStopwatch.Stop();

        throttledResult.IsSuccess.Should().BeTrue();
        throttledResult.Value.SucceededCount.Should().Be(throttledBatch.Length);
        throttledStopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(450),
            "two inter-batch delays of ~250ms each must have elapsed before a MaxBatchesPerSecond=4, " +
            "3-batch throttled call completes");

        var unthrottledBatch = Enumerable.Range(1, 6).Select(i => NewProduct($"unthrottled-{i}")).ToArray();
        var unthrottledStopwatch = Stopwatch.StartNew();
        var unthrottledResult = await index.IndexManyAsync(unthrottledBatch, SearchWriteConsistency.Accepted);
        unthrottledStopwatch.Stop();

        unthrottledResult.IsSuccess.Should().BeTrue();
        unthrottledStopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(450),
            "the unthrottled 3-arg overload must complete at today's speed, never delayed by a throttle it was never given");
        unthrottledStopwatch.Elapsed.Should().BeLessThan(
            throttledStopwatch.Elapsed,
            "the unthrottled call must be measurably faster than the throttled one — proof the pacing delay was real");
    }

    /// <summary>A document keyed on <c>sku</c> rather than <c>documentId</c> — for a dedicated real
    /// Meilisearch-engine-side task-failure test index (see <see cref="MixedBatchIndexName"/>).</summary>
    private sealed class SkuKeyedDocument : ISearchDocument
    {
        [JsonPropertyName("documentId")]
        public required string DocumentId { get; init; }

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; init; }

        // Omitted from the wire payload entirely when null — a real Meilisearch engine fails the
        // WHOLE task for a document missing its declared primary-key attribute.
        [JsonPropertyName("sku")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sku { get; init; }
    }

    [Fact]
    public async Task IndexManyAsync_MixedValidAndInvalidBatches_ProducesCorrectPartialReceipt_RegressionProofForC54()
    {
        const string mixedBatchIndexName = "sku-keyed-mixed-batch-tests";
        var skuDefinition = new SearchIndexDefinitionBuilder(mixedBatchIndexName)
            .PrimaryKey("sku")
            .Field("sku", SearchFieldKind.Keyword, filterable: true)
            .Build().Value;

        var options = MeilisearchProviderFactory.CreateOptions(_fixture);
        options.DefaultBatchSize = 1; // one document per dispatch batch — batch-level failure == per-document failure
        var client = MeilisearchProviderFactory.CreateClient(_fixture);
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(client, options);

        try
        {
            (await provisioner.EnsureIndexAsync(skuDefinition)).IsSuccess.Should().BeTrue();
            var index = MeilisearchProviderFactory.CreateIndex<SkuKeyedDocument>(client, skuDefinition, options);

            var documents = new[]
            {
                new SkuKeyedDocument { DocumentId = "sku-batch-valid-1", Sku = "SKU-001" },
                new SkuKeyedDocument { DocumentId = "sku-batch-invalid-1", Sku = null }, // missing primary key -> task fails
                new SkuKeyedDocument { DocumentId = "sku-batch-valid-2", Sku = "SKU-002" },
            };

            // Searchable consistency is required for the per-batch task-wait/failure-detection path to
            // run at all (MeilisearchIndex.IndexManyAsync only inspects task outcomes for this consistency).
            var result = await index.IndexManyAsync(documents, SearchWriteConsistency.Searchable);

            result.IsSuccess.Should().BeTrue("bulk partial failure is reported through the receipt, never as an outer Result.Failure");
            result.Value.SucceededCount.Should().Be(2);
            result.Value.HasFailures.Should().BeTrue();
            result.Value.Failures.Should().ContainSingle();
            result.Value.Failures[0].DocumentId.Should().Be("sku-batch-invalid-1");
            result.Value.Failures[0].Error.Code.Should().Be("search.meilisearch.indexing_task_failed");
        }
        finally
        {
            await provisioner.DeleteIndexAsync(mixedBatchIndexName);
        }
    }

    [Fact]
    public async Task IndexManyAsync_ThrottledMultiTenantBatchWithOneInvalidDocument_SurfacesItsOwnSearchItemFailure_SiblingsUnaffected()
    {
        const string tenantIsolationIndexName = "sku-keyed-throttle-tenant-isolation-tests";
        var skuDefinition = new SearchIndexDefinitionBuilder(tenantIsolationIndexName)
            .PrimaryKey("sku")
            .TenantField("tenantId")
            .Field("sku", SearchFieldKind.Keyword, filterable: true)
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
            .Build().Value;

        var options = MeilisearchProviderFactory.CreateOptions(_fixture);
        options.DefaultBatchSize = 1; // one document per dispatch batch — the throttle delay fires between each
        var client = MeilisearchProviderFactory.CreateClient(_fixture);
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(client, options);

        try
        {
            (await provisioner.EnsureIndexAsync(skuDefinition)).IsSuccess.Should().BeTrue();
            var index = MeilisearchProviderFactory.CreateIndex<SkuKeyedDocument>(client, skuDefinition, options);

            var documents = new[]
            {
                new SkuKeyedDocument { DocumentId = "tenant-a-valid-1", TenantId = "tenant-a", Sku = "SKU-A-001" },
                new SkuKeyedDocument { DocumentId = "tenant-b-invalid-1", TenantId = "tenant-b", Sku = null }, // missing primary key -> task fails
                new SkuKeyedDocument { DocumentId = "tenant-a-valid-2", TenantId = "tenant-a", Sku = "SKU-A-002" },
            };
            var bulkOptions = new SearchBulkWriteOptions { MaxBatchesPerSecond = 10 };

            var result = await index.IndexManyAsync(documents, SearchWriteConsistency.Searchable, bulkOptions);

            result.IsSuccess.Should().BeTrue();
            result.Value.SucceededCount.Should().Be(2);
            result.Value.HasFailures.Should().BeTrue();
            result.Value.Failures.Should().ContainSingle();
            result.Value.Failures[0].DocumentId.Should().Be("tenant-b-invalid-1");

            // The two valid, differently-tenanted siblings were never dropped by the pacing logic.
            // Queried by CountAsync per tenant rather than GetAsync, since Meilisearch's own document
            // identity for THIS index is the "sku" field, not our own DocumentId.
            var tenantACount = await index.CountAsync(filter: null, TenantScope.Of("tenant-a"));
            tenantACount.IsSuccess.Should().BeTrue();
            tenantACount.Value.Should().Be(2);
            var tenantBCount = await index.CountAsync(filter: null, TenantScope.Of("tenant-b"));
            tenantBCount.IsSuccess.Should().BeTrue();
            tenantBCount.Value.Should().Be(0, "the one tenant-b document failed to index and must not silently appear");
        }
        finally
        {
            await provisioner.DeleteIndexAsync(tenantIsolationIndexName);
        }
    }

    private static TestProduct NewProduct(string documentId) => new()
    {
        DocumentId = documentId,
        TenantId = TenantId,
        Name = "Throttle Test Product",
        Description = "A product created by a T-33 bulk-throttle test.",
        Status = "active",
        Category = "electronics",
        Price = 9.99,
        Stock = 1,
        InStock = true,
        CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
