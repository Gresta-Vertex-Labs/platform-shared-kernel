using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-14: real-backend read behaviour — filtered/sorted/faceted/paged search, facet truncation,
/// <see cref="TotalHitsAccuracy"/> mode selection, highlighting, tenant-scoped <c>GetAsync</c>, and
/// exact <c>CountAsync</c>.
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchReadBehaviourTests : IAsyncLifetime
{
    private const string IndexName = "products-read-behaviour-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchReadBehaviourTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task SearchAsync_FilteredSortedFacetedPaged_ReturnsExpectedHitsAndFacets()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var expectedElectronics = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .Where(p => p.Category == "electronics")
            .OrderBy(p => p.Price)
            .ToArray();

        var request = SearchRequest.Default with
        {
            Filter = SearchFilter.Eq(TestProductFields.Category, SearchValue.From("electronics")),
            Sort = [SearchSort.Ascending(TestProductFields.Price)],
            Facets = [TestProductFields.Status],
            Page = 1,
            PageSize = 2,
            RequireExactTotalHits = true,
        };

        var result = await index.SearchAsync(request, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalHits.Should().Be(expectedElectronics.Length);
        result.Value.Hits.Should().HaveCount(2);
        result.Value.Hits.Select(h => h.Document.DocumentId).Should().ContainInOrder(
            expectedElectronics.Take(2).Select(p => p.DocumentId));
        result.Value.Facets.Should().ContainKey(TestProductFields.Status);
        var statusFacet = result.Value.Facets[TestProductFields.Status];
        var expectedStatusCounts = expectedElectronics.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => (long)g.Count());
        foreach (var facetValue in statusFacet.Values)
        {
            expectedStatusCounts.Should().ContainKey(facetValue.Value);
            facetValue.Count.Should().Be(expectedStatusCounts[facetValue.Value]);
        }
    }

    [Fact]
    public async Task SearchAsync_FacetResult_TruncatesAtMaxFacetValuesCap()
    {
        var truncationIndexName = $"products-facet-truncation-{Guid.NewGuid():N}";
        var truncationDefinition = new SearchIndexDefinitionBuilder(truncationIndexName)
            .ConfigureSharedFields()
            .MaxFacetValues(2) // corpus has 3 distinct categories (electronics/furniture/stationery)
            .Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.EnsureIndexAsync(truncationDefinition);
        try
        {
            var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, truncationDefinition);
            await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable);

            var request = SearchRequest.Default with { Facets = [TestProductFields.Category], PageSize = 1 };
            var result = await index.SearchAsync(request, TenantScope.Of(TestProductCorpus.TenantA));

            result.IsSuccess.Should().BeTrue();
            result.Value.Facets[TestProductFields.Category].Values.Should().HaveCount(2);
            result.Value.Facets[TestProductFields.Category].Truncated.Should().BeTrue();
        }
        finally
        {
            await provisioner.DeleteIndexAsync(truncationIndexName);
        }
    }

    [Fact]
    public async Task SearchAsync_RequireExactTotalHitsFalse_YieldsEstimatedAccuracy()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var request = SearchRequest.Default with { RequireExactTotalHits = false };

        var result = await index.SearchAsync(request, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Accuracy.Should().Be(TotalHitsAccuracy.Estimated);
    }

    [Fact]
    public async Task SearchAsync_RequireExactTotalHitsTrue_YieldsExactAccuracy()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var request = SearchRequest.Default with { RequireExactTotalHits = true };

        var result = await index.SearchAsync(request, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Accuracy.Should().Be(TotalHitsAccuracy.Exact);
        result.Value.TotalHits.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
    }

    [Fact]
    public async Task SearchAsync_WithHighlight_PopulatesHighlightsWithoutFormattedMember()
    {
        // TestProduct declares no `_formatted`-shaped member — the provider must still populate
        // SearchHit.Highlights by reading Meilisearch's per-hit `_formatted` sibling object itself.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var request = SearchRequest.Default with
        {
            FreeText = "Wireless",
            Highlight = new HighlightRequest { Fields = [TestProductFields.Name] },
        };

        var result = await index.SearchAsync(request, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().NotBeEmpty();
        var hit = result.Value.Hits.Single(h => h.Document.DocumentId == "prod-001");
        hit.Highlights.Should().ContainKey(TestProductFields.Name);
        hit.Highlights[TestProductFields.Name].Should().Contain(h => h.Contains("<em>Wireless</em>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_OnTenantedIndex_CannotReadAnotherTenantsDocumentById()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var tenantBDoc = TestProductCorpus.ForTenant(TestProductCorpus.TenantB)[0];

        var result = await index.GetAsync(tenantBDoc.DocumentId, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.document_not_found");
    }

    [Fact]
    public async Task GetAsync_OwnTenant_ReturnsDocument()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var tenantADoc = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)[0];

        var result = await index.GetAsync(tenantADoc.DocumentId, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentId.Should().Be(tenantADoc.DocumentId);
    }

    [Fact]
    public async Task CountAsync_IsExact()
    {
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);

        var tenantACount = await index.CountAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantA));
        var tenantBCount = await index.CountAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantB));

        tenantACount.IsSuccess.Should().BeTrue();
        tenantACount.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
        tenantBCount.IsSuccess.Should().BeTrue();
        tenantBCount.Value.Should().Be(TestProductCorpus.ForTenant(TestProductCorpus.TenantB).Count);
    }
}
