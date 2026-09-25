using System.Diagnostics;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// T-32/T-34 (P-354, real-backend): against a real ElasticSearch container, a multi-batch bulk write
/// with <see cref="SearchBulkWriteOptions.MaxBatchesPerSecond"/> configured measurably paces batch
/// dispatch, while an unconfigured call completes at today's unthrottled speed — proving the default
/// behavior (the 3-arg <c>IndexManyAsync</c> overload delegating to <see cref="SearchBulkWriteOptions.Default"/>)
/// is unchanged (T-32). Also re-verifies that per-document bulk-item failure reporting is unaffected
/// by the new throttle: a deliberately-invalid document inside a throttled, multi-tenant, multi-batch
/// call still surfaces as its own <c>SearchItemFailure</c>, never silently dropped or swallowed by the
/// new pacing logic, and its differently-tenanted siblings are never dropped either (T-34).
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchBulkThrottleTests : IAsyncLifetime
{
    private const string IndexName = "products-bulk-throttle-tests";
    private const string TenantId = "throttle-tenant";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;

    public ElasticSearchBulkThrottleTests(ElasticsearchContainerFixture fixture)
    {
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        _definition = TestProductIndexDefinitions.Standard(IndexName);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
    }

    public async Task InitializeAsync()
    {
        var ensureResult = await _provisioner.EnsureIndexAsync(_definition);
        ensureResult.IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync() => await _provisioner.DeleteIndexAsync(IndexName);

    [Fact]
    public async Task IndexManyAsync_WithMaxBatchesPerSecondConfigured_MeasurablyPacesBatchDispatch_WhileUnconfiguredCallStaysFast()
    {
        // BulkMaxDocuments: 2 forces 6 documents into 3 dispatch batches (2 + 2 + 2), so the throttle's
        // inter-batch delay fires twice for a single IndexManyAsync call.
        var options = ElasticsearchProviderFactory.CreateOptions(o => o.BulkMaxDocuments = 2);
        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition, options: options);
        var throttledBatch = Enumerable.Range(1, 6).Select(i => NewProduct($"throttled-{i}")).ToArray();
        // 4 batches/sec => a 250ms delay before each non-first batch; two delays => >= 500ms of pacing.
        var bulkOptions = new SearchBulkWriteOptions { MaxBatchesPerSecond = 4 };

        var throttledStopwatch = Stopwatch.StartNew();
        var throttledResult = await index.IndexManyAsync(throttledBatch, SearchWriteConsistency.Accepted, bulkOptions);
        throttledStopwatch.Stop();

        throttledResult.IsSuccess.Should().BeTrue();
        throttledResult.Value.SucceededCount.Should().Be(throttledBatch.Length);
        throttledResult.Value.Failures.Should().BeEmpty();
        throttledStopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMilliseconds(450),
            "two inter-batch delays of ~250ms each must have elapsed before a MaxBatchesPerSecond=4, " +
            "3-batch throttled call completes");

        var unthrottledBatch = Enumerable.Range(1, 6).Select(i => NewProduct($"unthrottled-{i}")).ToArray();
        var unthrottledStopwatch = Stopwatch.StartNew();
        // The plain 3-arg overload — delegates to SearchBulkWriteOptions.Default internally.
        var unthrottledResult = await index.IndexManyAsync(unthrottledBatch, SearchWriteConsistency.Accepted);
        unthrottledStopwatch.Stop();

        unthrottledResult.IsSuccess.Should().BeTrue();
        unthrottledResult.Value.SucceededCount.Should().Be(unthrottledBatch.Length);
        unthrottledStopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(450),
            "the unthrottled 3-arg overload must complete at today's speed, never delayed by a throttle it was never given");
        unthrottledStopwatch.Elapsed.Should().BeLessThan(
            throttledStopwatch.Elapsed,
            "the unthrottled call must be measurably faster than the throttled one — proof the pacing delay was real, " +
            "not an artifact of general container/network latency");
    }

    private static TestProduct NewProduct(string documentId) => new()
    {
        DocumentId = documentId,
        TenantId = TenantId,
        Name = "Throttle Test Product",
        Description = "A product created by a T-32 bulk-throttle test.",
        Status = "active",
        Category = "electronics",
        Price = 9.99,
        Stock = 1,
        InStock = true,
        CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
    };

    /// <summary>
    /// A minimal document whose <c>stock</c> value is declared <see cref="object"/> so a test can hand
    /// it either a genuine number (indexes cleanly against the <c>stock</c> field's
    /// <see cref="SearchFieldKind.Integer"/> mapping) or a non-numeric string (a real ElasticSearch
    /// engine rejects THAT document with a per-item <c>mapper_parsing_exception</c>, independent of its
    /// siblings in the same bulk request) — for T-34's real, engine-observed per-document bulk-item
    /// failure, deliberately not the client-side <c>InvalidDocumentId</c> charset guard T-21 already
    /// covers.
    /// </summary>
    private sealed class ThrottleFailureDocument : ISearchDocument
    {
        [JsonPropertyName("documentId")]
        public required string DocumentId { get; init; }

        [JsonPropertyName("tenantId")]
        public required string TenantId { get; init; }

        [JsonPropertyName("stock")]
        public required object Stock { get; init; }
    }

    [Fact]
    public async Task IndexManyAsync_ThrottledMultiTenantBatchWithOneEngineRejectedDocument_SurfacesItsOwnSearchItemFailure_SiblingsUnaffected()
    {
        const string tenantIsolationIndexName = "products-bulk-throttle-tenant-isolation-tests";
        var definition = new SearchIndexDefinitionBuilder(tenantIsolationIndexName)
            .TenantField("tenantId")
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
            .Field("stock", SearchFieldKind.Integer, filterable: true)
            .Build().Value;

        var options = ElasticsearchProviderFactory.CreateOptions(o => o.BulkMaxDocuments = 1); // one document per dispatch batch
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client, options);

        try
        {
            (await provisioner.EnsureIndexAsync(definition)).IsSuccess.Should().BeTrue();
            var index = ElasticsearchProviderFactory.CreateIndex<ThrottleFailureDocument>(_client, definition, options: options);

            var documents = new[]
            {
                new ThrottleFailureDocument { DocumentId = "tenant-a-valid-1", TenantId = TestTenants.TenantA.ToString(), Stock = 10L },
                new ThrottleFailureDocument { DocumentId = "tenant-b-invalid-1", TenantId = TestTenants.TenantB.ToString(), Stock = "not-a-number" },
                new ThrottleFailureDocument { DocumentId = "tenant-a-valid-2", TenantId = TestTenants.TenantA.ToString(), Stock = 20L },
            };
            // Throttled — 3 documents, 1 per batch, so the pacing delay fires twice across this call.
            var bulkOptions = new SearchBulkWriteOptions { MaxBatchesPerSecond = 10 };

            var result = await index.IndexManyAsync(documents, SearchWriteConsistency.Searchable, bulkOptions);

            result.IsSuccess.Should().BeTrue("bulk partial failure is reported through the receipt, never as an outer Result.Failure");
            result.Value.SucceededCount.Should().Be(2);
            result.Value.HasFailures.Should().BeTrue();
            result.Value.Failures.Should().ContainSingle();
            result.Value.Failures[0].DocumentId.Should().Be("tenant-b-invalid-1");

            // The two valid, differently-tenanted siblings were never dropped by the pacing logic.
            (await index.GetAsync("tenant-a-valid-1", TenantScope.For(TestTenants.TenantA))).IsSuccess.Should().BeTrue();
            (await index.GetAsync("tenant-a-valid-2", TenantScope.For(TestTenants.TenantA))).IsSuccess.Should().BeTrue();
        }
        finally
        {
            await provisioner.DeleteIndexAsync(tenantIsolationIndexName);
        }
    }
}
