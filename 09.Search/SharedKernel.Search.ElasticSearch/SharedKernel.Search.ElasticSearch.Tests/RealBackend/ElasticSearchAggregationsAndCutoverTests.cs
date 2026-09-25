using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/SearchRequest<T> types;
// alias ours explicitly, mirroring the production translator's own convention.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// T-24: real-backend aggregations (<see cref="IAnalyticsSearch{TDocument}"/>) and provisioner cutover
/// against a live ElasticSearch 9.4.2 container.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchAggregationsAndCutoverTests : IAsyncLifetime
{
    private const string IndexName = "products-aggregations-tests";
    private const string CutoverStagingIndexName = "products-cutover-staging";
    private const string CutoverLiveAliasName = "products-cutover-live";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;
    private readonly ElasticSearchAnalytics<TestProduct> _analytics;

    public ElasticSearchAggregationsAndCutoverTests(ElasticsearchContainerFixture fixture)
    {
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        _definition = TestProductIndexDefinitions.Standard(IndexName);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        _analytics = ElasticsearchProviderFactory.CreateAnalytics<TestProduct>(_client, _definition);
    }

    public async Task InitializeAsync()
    {
        (await _provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        await _provisioner.DeleteIndexAsync(IndexName);
        // Best-effort cleanup for the cutover sub-test's own concrete staging index — deleting the
        // concrete index also removes any alias pointing at it, so nothing else needs cleaning up.
        // Ignored if the cutover test never ran or already deleted it.
        await _provisioner.DeleteIndexAsync(CutoverStagingIndexName);
    }

    [Fact]
    public async Task AggregateAsync_Terms_WithSubAggregation_ReturnsBucketsAndNestedStats()
    {
        var aggregations = new[]
        {
            AggregationRequest.Terms(
                "byCategory",
                TestProductFields.Category,
                size: 10,
                subAggregations: [AggregationRequest.Stats("avgPrice", TestProductFields.Price)]),
        };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetTerms("byCategory", out var terms).Should().BeTrue();

        var expectedElectronics = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .Where(p => p.Category == "electronics")
            .ToList();
        var electronicsBucket = terms!.Buckets.Should().ContainSingle(b => b.Key == "electronics").Subject;
        electronicsBucket.DocCount.Should().Be(expectedElectronics.Count);

        electronicsBucket.SubAggregations.TryGetStats("avgPrice", out var stats).Should().BeTrue();
        stats!.Count.Should().Be(expectedElectronics.Count);
        stats.Average.Should().BeApproximately(expectedElectronics.Average(p => p.Price), 0.01);
    }

    [Fact]
    public async Task AggregateAsync_Cardinality_ReturnsDistinctCategoryCount()
    {
        var aggregations = new[] { AggregationRequest.Cardinality("distinctCategories", TestProductFields.Category) };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetCardinality("distinctCategories", out var cardinality).Should().BeTrue();
        cardinality!.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.Category).Distinct().Count());
    }

    [Fact]
    public async Task AggregateAsync_Stats_MatchesLinqComputedValues()
    {
        var aggregations = new[] { AggregationRequest.Stats("priceStats", TestProductFields.Price) };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetStats("priceStats", out var stats).Should().BeTrue();

        var tenantAPrices = TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.Price).ToList();
        stats!.Count.Should().Be(tenantAPrices.Count);
        stats.Min.Should().BeApproximately(tenantAPrices.Min(), 0.001);
        stats.Max.Should().BeApproximately(tenantAPrices.Max(), 0.001);
        stats.Average.Should().BeApproximately(tenantAPrices.Average(), 0.001);
        stats.Sum.Should().BeApproximately(tenantAPrices.Sum(), 0.001);
    }

    [Fact]
    public async Task AggregateAsync_DateHistogram_ByMonth_MatchesLinqComputedBuckets()
    {
        var aggregations = new[]
        {
            AggregationRequest.DateHistogram("byMonth", TestProductFields.CreatedAt, DateHistogramInterval.Month),
        };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetDateHistogram("byMonth", out var histogram).Should().BeTrue();

        var expectedByMonth = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .GroupBy(p => new DateTimeOffset(p.CreatedAt.Year, p.CreatedAt.Month, 1, 0, 0, 0, TimeSpan.Zero))
            .ToDictionary(g => g.Key, g => (long)g.Count());

        histogram!.Buckets.Where(b => b.DocCount > 0).Should().HaveCount(expectedByMonth.Count);
        foreach (var bucket in histogram.Buckets.Where(b => b.DocCount > 0))
        {
            expectedByMonth.Should().ContainKey(bucket.Key);
            expectedByMonth[bucket.Key].Should().Be(bucket.DocCount);
        }
    }

    [Fact]
    public async Task AggregateAsync_Range_MatchesLinqComputedBuckets()
    {
        var ranges = new[]
        {
            new AggregationBucketRange("cheap", null, 50),
            new AggregationBucketRange("expensive", 50, null),
        };
        var aggregations = new[] { AggregationRequest.Range("priceRanges", TestProductFields.Price, ranges) };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetRange("priceRanges", out var rangeResult).Should().BeTrue();

        var tenantAPrices = TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Select(p => p.Price).ToList();
        var expectedCheap = tenantAPrices.Count(p => p < 50);
        var expectedExpensive = tenantAPrices.Count(p => p >= 50);

        rangeResult!.Buckets.Should().ContainSingle(b => b.Key == "cheap").Which.DocCount.Should().Be(expectedCheap);
        rangeResult.Buckets.Should().ContainSingle(b => b.Key == "expensive").Which.DocCount.Should().Be(expectedExpensive);
    }

    [Fact]
    public async Task TryGetCardinality_OnATermsAggregationName_ReturnsFalse_ForAMismatchedRead()
    {
        var aggregations = new[] { AggregationRequest.Terms("byCategory", TestProductFields.Category, size: 10) };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TryGetCardinality("byCategory", out var mismatched).Should().BeFalse();
        mismatched.Should().BeNull();
    }

    [Fact]
    public async Task AggregateAsync_TenantScoped_ExcludesOtherTenantsDocuments()
    {
        var aggregations = new[]
        {
            AggregationRequest.Terms("byCategory", TestProductFields.Category, size: 10),
            AggregationRequest.Cardinality("distinctCategories", TestProductFields.Category),
        };

        var result = await _analytics.AggregateAsync(filter: null, aggregations, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();

        // Both tenants happen to share the same 3 category values in this corpus, so cardinality alone
        // cannot prove exclusion — the doc-count SUM is the real proof.
        result.Value.TryGetCardinality("distinctCategories", out var cardinality).Should().BeTrue();
        cardinality!.Value.Should().Be(3);

        result.Value.TryGetTerms("byCategory", out var terms).Should().BeTrue();
        var totalDocCount = terms!.Buckets.Sum(b => b.DocCount);
        totalDocCount.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
        totalDocCount.Should().NotBe(TestProductCorpus.All.Count);
    }

    [Fact]
    public async Task CutoverAsync_FlipsReadAlias_LiveIndexServesStagedDataImmediately_NoUndefinedAliasWindow()
    {
        // Step 1: EnsureIndexAsync creates the REAL concrete staging index.
        var stagingDefinition = TestProductIndexDefinitions.Standard(CutoverStagingIndexName);
        (await _provisioner.EnsureIndexAsync(stagingDefinition)).IsSuccess.Should().BeTrue();

        // Step 2: write directly into the concrete staging index — writeAlias == definition.Name here,
        // since no alias exists yet at this point.
        var stagingIndex = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, stagingDefinition);
        (await stagingIndex.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();

        // Step 3: a single UpdateAliasesAsync call flips CutoverLiveAliasName onto the staging index.
        var cutoverResult = await _provisioner.CutoverAsync(new IndexCutoverRequest
        {
            StagingIndexName = CutoverStagingIndexName,
            LiveIndexName = CutoverLiveAliasName,
            DeleteStagingAfterCutover = false,
        });
        cutoverResult.IsSuccess.Should().BeTrue();

        // Step 4: read through a NEW ElasticSearchIndex targeting the read ALIAS. An immediate,
        // no-retry success here IS the "no undefined-alias window" proof.
        var liveDefinition = TestProductIndexDefinitions.Standard(CutoverLiveAliasName);
        var liveIndex = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, liveDefinition);

        var searchResult = await liveIndex.SearchAsync(SearchRequest.Default, TenantScope.For(TestProductCorpus.TenantA));
        searchResult.IsSuccess.Should().BeTrue();
        searchResult.Value.TotalHits.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);

        var getResult = await liveIndex.GetAsync("prod-001", TenantScope.For(TestProductCorpus.TenantA));
        getResult.IsSuccess.Should().BeTrue();
    }
}
