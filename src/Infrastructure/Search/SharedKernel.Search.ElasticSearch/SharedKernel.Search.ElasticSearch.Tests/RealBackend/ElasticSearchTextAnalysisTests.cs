using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// Real-backend tests for the index-level synonym and stop-word declarations and for
/// <c>VerifyRegisteredIndexesAsync</c>, both added by the pre-publish pass.
/// </summary>
/// <remarks>
/// The cross-provider point these pin down is that the identical neutral declaration produces the
/// identical observable behaviour on both engines — the Meilisearch sibling has a matching suite. The
/// mechanisms are entirely different (Meilisearch's <c>synonyms</c> index setting versus an
/// ElasticSearch <c>synonym</c> token filter inside a custom analyzer), which is exactly why the
/// behaviour, not the mechanism, is what is asserted.
/// </remarks>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchTextAnalysisTests : IAsyncLifetime
{
    private const string IndexName = "products-text-analysis-tests";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["rodent"] = ["mouse"] };

    private readonly ElasticsearchContainerFixture _fixture;
    private ElasticsearchClient _client = null!;
    private SearchIndexDefinition _definition = null!;

    public ElasticSearchTextAnalysisTests(ElasticsearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _client = ElasticsearchProviderFactory.CreateClient(_fixture);
        // Identical declaration to the Meilisearch suite: one-way, rodent => mouse.
        _definition = TestProductIndexDefinitions.WithTextAnalysis(
            IndexName, Synonyms, ["the", "a", "with"]);

        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();

        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();
    }

    public async Task DisposeAsync()
    {
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task DeclaredSynonym_MakesTheEngineMatchTheReplacementTerm()
    {
        // "Wireless Mouse" contains no occurrence of "rodent". It is found only because the synonym
        // token filter built from the neutral declaration reached the index's analysis settings.
        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);

        var result = await index.SearchAsync(
            SearchRequest.Default with { FreeText = "rodent", PageSize = 20 },
            TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().Contain(
            hit => hit.Document.Name == "Wireless Mouse",
            "the declared synonym must reach the engine's analysis chain, not merely the definition object");
    }

    [Fact]
    public async Task DeclaredStopWord_StopsContributingToMatching()
    {
        // "the" is declared a stop word, so a query consisting only of stop words matches nothing rather
        // than matching every document that happens to contain them.
        var index = ElasticsearchProviderFactory.CreateIndex<TestProduct>(_client, _definition);

        var result = await index.SearchAsync(
            SearchRequest.Default with { FreeText = "the", PageSize = 20 },
            TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().BeEmpty("every term in the query was removed by the stop filter");
    }

    [Fact]
    public async Task VerifyRegisteredIndexesAsync_WhenTheLiveIndexMatches_Succeeds()
    {
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(
            _client,
            registeredDefinitions: new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                [IndexName] = _definition,
            });

        var result = await provisioner.VerifyRegisteredIndexesAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyRegisteredIndexesAsync_WhenAStopWordWasEditedWithoutARebuild_ReportsDrift()
    {
        var driftedDefinition = TestProductIndexDefinitions.WithTextAnalysis(
            IndexName, Synonyms, ["the", "a"]);

        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(
            _client,
            registeredDefinitions: new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                [IndexName] = driftedDefinition,
            });

        var result = await provisioner.VerifyRegisteredIndexesAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.probe_failed");
        result.Error.Message.Should().Contain("fingerprint");
    }

    [Fact]
    public async Task EnsureIndexAsync_OnALiveIndexWithChangedTextAnalysis_ReturnsConflict()
    {
        // ElasticSearch cannot change an open index's analysis settings at all, so this is a hard engine
        // constraint here — and the Meilisearch sibling refuses the same change even though its own
        // engine would allow it, so that the two providers stay observably identical.
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        var changed = TestProductIndexDefinitions.WithTextAnalysis(
            IndexName,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["rodent"] = ["mouse", "vermin"] },
            ["the", "a", "with"]);

        var result = await provisioner.EnsureIndexAsync(changed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.index_definition_conflict");
        result.Error.Message.Should().Contain("CutoverAsync");
    }
}
