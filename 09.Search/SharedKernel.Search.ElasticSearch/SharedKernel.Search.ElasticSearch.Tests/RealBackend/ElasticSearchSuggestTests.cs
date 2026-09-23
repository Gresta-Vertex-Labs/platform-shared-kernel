using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Suggest;
using SharedKernel.Search.ElasticSearch.Tests.Containers;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.ElasticSearch.Tests.RealBackend;

/// <summary>
/// Real-backend tests for <c>ISuggestSearch&lt;TDocument&gt;</c> — the ElasticSearch-exclusive
/// completion suggester added by the pre-publish pass as the deliberate counterpart to Meilisearch's
/// <c>IInstantSearch&lt;TDocument&gt;</c>.
/// </summary>
/// <remarks>
/// The tenant-isolation test is the important one. ElasticSearch's completion suggester answers from
/// its own FST and ignores the surrounding query entirely, so a query-level tenant filter has no effect
/// on it — the tenant must travel as a category context registered on the completion mapping at
/// provisioning time. Without that mechanism this capability could not have shipped for a tenanted
/// index at all, because there would be no way to stop one tenant's content completing another
/// tenant's typing.
/// </remarks>
[Collection(ElasticsearchCollection.Name)]
public sealed class ElasticSearchSuggestTests : IAsyncLifetime
{
    private const string IndexName = "products-suggest-tests";

    private readonly ElasticsearchContainerFixture _fixture;
    private ElasticsearchClient _client = null!;
    private SearchIndexDefinition _definition = null!;
    private ISuggestSearch<SuggestableProduct> _suggest = null!;

    public ElasticSearchSuggestTests(ElasticsearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _client = ElasticsearchProviderFactory.CreateClient(_fixture);
        _definition = new SearchIndexDefinitionBuilder(IndexName)
            .PrimaryKey(TestProductFields.DocumentId)
            .TenantField(TestProductFields.TenantId)
            .Field(TestProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
            .Field(TestProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
            .Field(TestProductFields.Name, SearchFieldKind.Text, searchable: true)
            .Build()
            .Value;

        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(
            _client,
            completionFields: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [IndexName] = [SuggestableProduct.SuggestField],
            });

        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();

        var index = ElasticsearchProviderFactory.CreateIndex<SuggestableProduct>(_client, _definition);
        var documents = new SuggestableProduct[]
        {
            new() { DocumentId = "sug-1", TenantId = "tenant-a", Name = "Wireless Mouse", NameSuggest = "Wireless Mouse" },
            new() { DocumentId = "sug-2", TenantId = "tenant-a", Name = "Wireless Keyboard", NameSuggest = "Wireless Keyboard" },
            new() { DocumentId = "sug-3", TenantId = "tenant-b", Name = "Wireless Headphones", NameSuggest = "Wireless Headphones" },
        };
        (await index.IndexManyAsync(documents, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();

        _suggest = ElasticsearchProviderFactory.CreateSuggestSearch<SuggestableProduct>(
            _client, _definition, [SuggestableProduct.SuggestField]);
    }

    public async Task DisposeAsync()
    {
        var provisioner = ElasticsearchProviderFactory.CreateProvisioner(_client);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task SuggestAsync_ReturnsCompletionsForThePrefix()
    {
        var result = await _suggest.SuggestAsync(
            SuggestableProduct.SuggestField, "Wireless", TenantScope.Of("tenant-a"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value.Should().OnlyContain(s => s.Text.StartsWith("Wireless", StringComparison.Ordinal));
        result.Value.Should().OnlyContain(s => !string.IsNullOrEmpty(s.DocumentId));
    }

    [Fact]
    public async Task SuggestAsync_NeverCompletesAcrossTenants()
    {
        // "Wireless Headphones" belongs to tenant-b. A tenant-a caller must never see it, however it is
        // typed — the completion context is the only thing preventing that, since the suggester ignores
        // query filters entirely.
        var result = await _suggest.SuggestAsync(
            SuggestableProduct.SuggestField, "Wireless", TenantScope.Of("tenant-a"), size: 50);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain(
            s => s.Text.Contains("Headphones", StringComparison.Ordinal),
            "a suggestion derived from another tenant's document is a cross-tenant leak as the user types");
    }

    [Fact]
    public async Task SuggestAsync_WithTenantScopeNoneOnATenantedIndex_FailsClosed()
    {
        var result = await _suggest.SuggestAsync(
            SuggestableProduct.SuggestField, "Wireless", TenantScope.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.tenant_scope_missing");
    }

    [Fact]
    public async Task SuggestAsync_ForAFieldNeverDeclaredAsACompletionField_FailsAtTheCall()
    {
        // A field that was never declared produces an opaque ElasticSearch mapping error, and the
        // caller's real mistake — forgetting WithCompletionField at the composition root — is not
        // recoverable from it.
        var result = await _suggest.SuggestAsync("notDeclared", "Wireless", TenantScope.Of("tenant-a"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.field_not_searchable");
        result.Error.Message.Should().Contain("WithCompletionField");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SuggestAsync_WithABlankPrefix_IsRejected(string prefix)
    {
        var result = await _suggest.SuggestAsync(
            SuggestableProduct.SuggestField, prefix, TenantScope.Of("tenant-a"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_request");
    }

    [Fact]
    public async Task SuggestAsync_WithFuzzyEnabled_ToleratesATypo()
    {
        // Opt-in, because ElasticSearch's length-scaled edit distance costs materially more than an
        // exact prefix walk over the FST.
        var result = await _suggest.SuggestAsync(
            SuggestableProduct.SuggestField, "Wirelss", TenantScope.Of("tenant-a"), fuzzy: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty("fuzzy completion must tolerate a single-character typo");
    }
}
