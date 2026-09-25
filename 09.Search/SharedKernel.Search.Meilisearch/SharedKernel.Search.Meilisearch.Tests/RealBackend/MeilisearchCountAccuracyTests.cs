using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// Real-backend regression tests for the defect the pre-publish pass fixed: <c>CountAsync</c> returned
/// a bare <see cref="long"/> read from a paginated search's <c>totalHits</c>, which Meilisearch caps at
/// the index's <c>pagination.maxTotalHits</c>. An index holding more matching documents than that
/// ceiling therefore reported the ceiling as if it were the true total — silently, and differently from
/// the ElasticSearch sibling, whose <c>_count</c> API is uncapped.
/// </summary>
/// <remarks>
/// The ceiling is set deliberately low (<see cref="CeilingMaxTotalHits"/>) rather than indexing more
/// than the 1000-document platform default, so the test proves the behaviour against the real engine in
/// under a second. The ceiling itself is not special — what matters is that the count reaches it.
/// </remarks>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchCountAccuracyTests : IAsyncLifetime
{
    private const string IndexName = "products-count-accuracy-tests";
    private const int CeilingMaxTotalHits = 5;

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchCountAccuracyTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName)
            .ConfigureSharedFields()
            .MaxTotalHits(CeilingMaxTotalHits)
            .Build()
            .Value;

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
    public async Task CountAsync_WhenMatchesExceedTheEngineCeiling_ReportsALowerBound_NotAFabricatedExactTotal()
    {
        // tenant-a holds 10 documents against a maxTotalHits of 5. Meilisearch will answer 5.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var tenantADocumentCount = TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count;
        tenantADocumentCount.Should().BeGreaterThan(
            CeilingMaxTotalHits, "the test is meaningless unless the corpus exceeds the ceiling");

        var result = await index.CountAsync(filter: null, TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsExact.Should().BeFalse(
            "Meilisearch truncated the total at maxTotalHits, so reporting it as exact would publish a " +
            "wrong number as fact — the defect this test exists to prevent regressing");
        result.Value.Accuracy.Should().Be(TotalHitsAccuracy.LowerBound);
        result.Value.Value.Should().Be(CeilingMaxTotalHits);
        result.Value.ToString().Should().Be($">={CeilingMaxTotalHits}");
    }

    [Fact]
    public async Task CountAsync_WhenMatchesAreBelowTheEngineCeiling_IsExact()
    {
        // The same index, the same ceiling, but a filter narrow enough that the true count is under it:
        // the ceiling must not make every count on a low-maxTotalHits index unusable.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var expected = TestProductCorpus.ForTenant(TestProductCorpus.TenantA)
            .Count(p => p.Category == "stationery");
        expected.Should().BeLessThan(CeilingMaxTotalHits);

        var result = await index.CountAsync(
            SearchFilter.Eq(TestProductFields.Category, SearchValue.From("stationery")),
            TenantScope.Of(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsExact.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Fact]
    public async Task CountAsync_StillFailsClosedWithoutATenantScope()
    {
        // The accuracy change must not have weakened the tenant guard that runs before any I/O.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);

        var result = await index.CountAsync(filter: null, TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.tenant_scope_missing");
    }
}
