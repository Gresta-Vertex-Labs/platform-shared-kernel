using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Index;
using SharedKernel.Search.ElasticSearch.Provisioning;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
// Elastic.Clients.Elasticsearch declares its own non-generic SearchRequest/SearchRequest<T> types;
// alias ours explicitly, mirroring the production translator's own convention.
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// T-22: real-backend read behaviour against a live ElasticSearch 9.4.2 container — filtered/sorted/
/// faceted/paged search, <c>hits.total.relation</c> accuracy mapping, highlighting, tenant-scoped
/// <c>GetAsync</c>, exact <c>CountAsync</c>, and the startup engine-version guard.
/// </summary>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchReadBehaviourTests : IAsyncLifetime
{
    private const string IndexName = "products-read-tests";
    private const string FacetTruncationIndexName = "products-read-facet-truncation-tests";

    private readonly ElasticsearchClient _client;
    private readonly SearchIndexDefinition _definition;
    private readonly ElasticSearchIndexProvisioner _provisioner;
    private readonly ElasticSearchIndex<TestProduct> _index;
    private readonly SearchIndexDefinition _facetTruncationDefinition;
    private readonly ElasticSearchIndex<TestProduct> _facetTruncationIndex;

    public ElasticSearchReadBehaviourTests(ElasticsearchContainerFixture fixture)
    {
        _client = ElasticsearchProviderFactory.CreateClient(fixture);
        _definition = TestProductIndexDefinitions.Standard(IndexName);
        _provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        _index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);

        // A dedicated, small MaxFacetValues=2 index (3 distinct categories in the corpus) so
        // FacetResult.Truncated is genuinely exercised, per the phase's own instruction.
        _facetTruncationDefinition = TestProductIndexDefinitions.WithMaxFacetValues(FacetTruncationIndexName, 2);
        _facetTruncationIndex = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _facetTruncationDefinition);
    }

    public async Task InitializeAsync()
    {
        (await _provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        (await _index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();

        (await _provisioner.EnsureIndexAsync(_facetTruncationDefinition)).IsSuccess.Should().BeTrue();
        (await _facetTruncationIndex.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        await _provisioner.DeleteIndexAsync(IndexName);
        await _provisioner.DeleteIndexAsync(FacetTruncationIndexName);
    }

    [Fact]
    public async Task SearchAsync_FilteredSortedPaged_ReturnsExpectedDocumentsInOrder()
    {
        var request = new SearchRequest
        {
            Filter = SearchFilter.Eq(TestProductFields.Category, SearchValue.From("electronics")),
            Sort = [SearchSort.Ascending(TestProductFields.Price)],
            Page = 1,
            PageSize = 2,
        };

        var result = await _index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        var expected = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .Where(p => p.Category == "electronics")
            .OrderBy(p => p.Price)
            .ToList();

        result.Value.TotalHits.Should().Be(expected.Count);
        result.Value.Hits.Should().HaveCount(2);
        result.Value.Hits.Select(h => h.Document.DocumentId).Should().Equal(expected.Take(2).Select(p => p.DocumentId));
    }

    [Fact]
    public async Task SearchAsync_WithFacets_ReturnsCorrectValueCounts()
    {
        var request = new SearchRequest { Facets = [TestProductFields.Status] };

        var result = await _index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Facets.Should().ContainKey(TestProductFields.Status);

        var expectedCounts = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .GroupBy(p => p.Status)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var facet = result.Value.Facets[TestProductFields.Status];
        facet.Truncated.Should().BeFalse();
        foreach (var facetValue in facet.Values)
        {
            expectedCounts.Should().ContainKey(facetValue.Value);
            expectedCounts[facetValue.Value].Should().Be(facetValue.Count);
        }

        facet.Values.Select(v => v.Value).Should().BeEquivalentTo(expectedCounts.Keys);
    }

    [Fact]
    public async Task SearchAsync_WithMaxFacetValuesCap_TruncatesFacetValues()
    {
        var request = new SearchRequest { Facets = [TestProductFields.Category] };

        var result = await _facetTruncationIndex.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Facets.Should().ContainKey(TestProductFields.Category);

        var facet = result.Value.Facets[TestProductFields.Category];
        // The corpus has 3 distinct categories (electronics/furniture/stationery); this index caps
        // MaxFacetValues at 2, so the cap is genuinely exercised.
        facet.Truncated.Should().BeTrue();
        facet.Values.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_WithRequireExactTotalHits_ReturnsExactAccuracy()
    {
        var request = new SearchRequest { RequireExactTotalHits = true };

        var result = await _index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Accuracy.Should().Be(TotalHitsAccuracy.Exact);
        result.Value.TotalHits.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
    }

    [Fact]
    public async Task SearchAsync_WithDefaultRequireExactTotalHits_NeverYieldsEstimatedAccuracy()
    {
        // ElasticSearch has no "estimated total" concept at all — TotalHitsAccuracy.Estimated is
        // Meilisearch-only. The default (false) still yields a valid Exact-or-LowerBound value; at this
        // corpus's scale (well under ES's 10 000 track_total_hits default) it is always Exact in
        // practice, but the assertion is written against the honest contract, not the incidental scale.
        var request = SearchRequest.Default;

        var result = await _index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Accuracy.Should().NotBe(TotalHitsAccuracy.Estimated);
        result.Value.Accuracy.Should().BeOneOf(TotalHitsAccuracy.Exact, TotalHitsAccuracy.LowerBound);
    }

    [Fact]
    public async Task SearchAsync_WithHighlightRequest_PopulatesHighlightsWithEmTags()
    {
        var request = new SearchRequest
        {
            FreeText = "wireless",
            Highlight = new HighlightRequest { Fields = [TestProductFields.Name] },
        };

        var result = await _index.SearchAsync(request, TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        var hit = result.Value.Hits.Should().ContainSingle(h => h.Document.DocumentId == "prod-001").Subject;
        hit.Highlights.Should().ContainKey(TestProductFields.Name);
        hit.Highlights[TestProductFields.Name].Should().Contain(fragment => fragment.Contains("<em>") && fragment.Contains("</em>"));
    }

    [Fact]
    public async Task GetAsync_TenantB_CannotReadTenantADocument_ReturnsDocumentNotFound()
    {
        var result = await _index.GetAsync("prod-001", TenantScope.For(TestProductCorpus.TenantB));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.document_not_found");
    }

    [Fact]
    public async Task GetAsync_OwningTenant_ReturnsDocument()
    {
        var result = await _index.GetAsync("prod-011", TenantScope.For(TestProductCorpus.TenantB));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentId.Should().Be("prod-011");
    }

    [Fact]
    public async Task CountAsync_MatchesTenantCorpusCount_Exactly()
    {
        var countA = await _index.CountAsync(filter: null, TenantScope.For(TestProductCorpus.TenantA));
        countA.IsSuccess.Should().BeTrue();
        countA.Value.IsExact.Should().BeTrue("the ElasticSearch _count API has no maxTotalHits ceiling, so this provider is always exact");
        countA.Value.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);

        var countB = await _index.CountAsync(filter: null, TenantScope.For(TestProductCorpus.TenantB));
        countB.IsSuccess.Should().BeTrue();
        countB.Value.IsExact.Should().BeTrue();
        countB.Value.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantB).Count);
    }

    /// <summary>
    /// The engine-version check is now an explicit, asynchronous call
    /// (<c>VerifyElasticSearchEngineVersionAsync</c>) rather than a blocking side effect of resolving
    /// the <see cref="ElasticsearchClient"/> singleton. This test pins both halves of that change:
    /// resolving the client performs no network I/O and therefore cannot block or fail on an
    /// unreachable cluster, and the explicit call against an unreachable node returns a failed
    /// <c>Result</c> carrying <c>search.unreachable</c> instead of merely logging.
    /// </summary>
    /// <remarks>
    /// It points at <c>http://127.0.0.1:1/</c> — a guaranteed-unreachable, syntactically valid URL
    /// (nothing listens on port 1) — rather than standing up a second real ES container, which this
    /// domain's rules forbid as a competing ad hoc container setup.
    /// </remarks>
    [Fact]
    public async Task VerifyEngineVersion_UnreachableNode_FailsWithUnreachable_AndClientResolutionNeverBlocks()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton<IClock>(new FakeClock());

        var configValues = new Dictionary<string, string?>
        {
            ["Search:ElasticSearch:Nodes:0"] = "http://127.0.0.1:1/",
            ["Search:ElasticSearch:RequestTimeoutSeconds"] = "1",
            ["Search:ElasticSearch:PingTimeoutSeconds"] = "1",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        services.AddSharedKernelElasticSearchSearch(configuration)
            .AddIndex<TestProduct>(
                "guard-read", "guard-write", b => b.Field(TestProductFields.Name, SearchFieldKind.Text, searchable: true))
            .Build();

        var provider = services.BuildServiceProvider();

        // Resolving the client is pure construction — no Info round trip, so an unreachable cluster
        // cannot stall a request that happens to be the first to resolve it.
        var act = () => provider.GetRequiredService<ElasticsearchClient>();
        act.Should().NotThrow();

        var result = await provider.VerifyElasticSearchEngineVersionAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.unreachable");

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var clientLogger = loggerFactory.GetLogger(typeof(ElasticsearchClient).FullName!);
        clientLogger.Records.ShouldHaveLogged(new EventId(9202), LogLevel.Error);
    }
}

