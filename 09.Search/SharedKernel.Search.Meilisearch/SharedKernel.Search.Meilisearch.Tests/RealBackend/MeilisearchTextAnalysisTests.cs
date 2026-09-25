using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// Real-backend tests for the index-level synonym and stop-word declarations, the Meilisearch-exclusive
/// ranking rules, and <c>VerifyRegisteredIndexesAsync</c> — all added by the pre-publish pass.
/// </summary>
/// <remarks>
/// Synonyms and stop words are asserted by their <em>observable search behaviour</em> against the real
/// engine rather than by reading the settings back: a settings round trip would prove only that this
/// provider sent what it meant to send, not that the engine does anything with it.
/// </remarks>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchTextAnalysisTests : IAsyncLifetime
{
    private const string IndexName = "products-text-analysis-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;

    public MeilisearchTextAnalysisTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName)
            .ConfigureSharedFields()
            // "mouse" also matches documents containing "rodent"; one-way, so the reverse does not hold.
            .Synonym("rodent", "mouse")
            .StopWords("the", "a", "with")
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
    public async Task DeclaredSynonym_MakesTheEngineMatchTheReplacementTerm()
    {
        // "Wireless Mouse" contains no occurrence of "rodent". It is found only because the engine
        // expands the query through the synonym declared on the index definition.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);

        var result = await index.SearchAsync(
            SearchRequest.Default with { FreeText = "rodent", PageSize = 20 },
            TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().Contain(
            hit => hit.Document.Name == "Wireless Mouse",
            "the declared synonym rodent => mouse must reach the engine, not merely the definition object");
    }

    [Fact]
    public async Task SynonymsAreOneWay_SoTheReverseDirectionIsNotImplied()
    {
        // Declaring rodent => mouse does NOT imply mouse => rodent. This asymmetry is deliberate and is
        // what makes the declaration portable: Meilisearch's synonyms setting is natively one-way, and
        // ElasticSearch is emitted in the equivalent one-way explicit-mapping form. Anything else would
        // behave differently per engine.
        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);

        var result = await index.SearchAsync(
            SearchRequest.Default with { FreeText = "mouse", PageSize = 20 },
            TenantScope.For(TestProductCorpus.TenantA));

        result.IsSuccess.Should().BeTrue();
        result.Value.Hits.Should().NotBeEmpty("the literal term still matches");
    }

    [Fact]
    public async Task VerifyRegisteredIndexesAsync_WhenTheLiveIndexMatches_Succeeds()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(
            _fixture,
            registeredDefinitions: new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                [IndexName] = _definition,
            });

        var result = await provisioner.VerifyRegisteredIndexesAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyRegisteredIndexesAsync_WhenASynonymWasEditedWithoutARebuild_ReportsDrift()
    {
        // The deployment failure this member exists to catch: code ships declaring a synonym list the
        // live index was never rebuilt for, so relevance silently differs from what the service expects.
        // Nothing else in the domain notices — the index exists, is addressable, and searches fine.
        var driftedDefinition = new SearchIndexDefinitionBuilder(IndexName)
            .ConfigureSharedFields()
            .Synonym("rodent", "mouse", "vermin")
            .StopWords("the", "a", "with")
            .Build()
            .Value;

        var provisioner = MeilisearchProviderFactory.CreateProvisioner(
            _fixture,
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
    public async Task VerifyRegisteredIndexesAsync_WhenAnIndexWasNeverProvisioned_ReportsDrift()
    {
        var neverProvisioned = new SearchIndexDefinitionBuilder("index-that-does-not-exist")
            .ConfigureSharedFields()
            .Build()
            .Value;

        var provisioner = MeilisearchProviderFactory.CreateProvisioner(
            _fixture,
            registeredDefinitions: new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                ["index-that-does-not-exist"] = neverProvisioned,
            });

        var result = await provisioner.VerifyRegisteredIndexesAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.probe_failed");
    }

    [Fact]
    public async Task VerifyRegisteredIndexesAsync_WithNoRegisteredIndexes_Succeeds()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);

        var result = await provisioner.VerifyRegisteredIndexesAsync();

        result.IsSuccess.Should().BeTrue("a service that registers no index has nothing to verify");
    }

    [Fact]
    public async Task EnsureIndexAsync_OnALiveIndexWithChangedTextAnalysis_ReturnsConflict_NeverRewritesInPlace()
    {
        // Meilisearch would happily rewrite synonyms on a live index; ElasticSearch cannot change an
        // open index's analysis settings at all. Honouring the change here while the sibling refuses it
        // would reintroduce exactly the cross-provider divergence this domain exists to prevent, so both
        // refuse and both point at the same remedy: staging index, bulk load, cutover.
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        var changed = new SearchIndexDefinitionBuilder(IndexName)
            .ConfigureSharedFields()
            .Synonym("rodent", "mouse")
            .StopWords("the", "a")
            .Build()
            .Value;

        var result = await provisioner.EnsureIndexAsync(changed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.index_definition_conflict");
        result.Error.Message.Should().Contain("CutoverAsync");
    }

    [Fact]
    public async Task RankingRules_AreAppliedWhenDeclaredForTheIndex()
    {
        // Ranking rules are Meilisearch-exclusive — ElasticSearch has no ordered tie-breaker list — so
        // they are declared in the provider package, not on the neutral SearchIndexDefinition. This
        // asserts the plumbing reaches the engine: an index provisioned with a custom sequence accepts
        // it, where a malformed rule would be rejected by the engine's own settings endpoint.
        const string rankingIndexName = "products-ranking-rules-tests";
        var definition = new SearchIndexDefinitionBuilder(rankingIndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(
            _fixture,
            registeredDefinitions: new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal)
            {
                [rankingIndexName] = definition,
            });

        try
        {
            var rules = new[]
            {
                MeilisearchRankingRule.Words,
                MeilisearchRankingRule.Typo,
                MeilisearchRankingRule.Proximity,
                MeilisearchRankingRule.Attribute,
                MeilisearchRankingRule.Sort,
                MeilisearchRankingRule.Exactness,
                MeilisearchRankingRule.Descending(TestProductFields.Price),
            };

            rules[^1].Value.Should().Be($"{TestProductFields.Price}:desc");

            var withRules = MeilisearchProviderFactory.CreateProvisionerWithRankingRules(
                _fixture, definition, rules.Select(r => r.Value).ToArray());

            var result = await withRules.EnsureIndexAsync(definition);

            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            await provisioner.DeleteIndexAsync(rankingIndexName);
        }
    }
}
