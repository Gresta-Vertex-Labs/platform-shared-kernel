using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// Builds the shared <see cref="TestProduct"/> index-definition field shape under a caller-supplied
/// index name — factored out of <see cref="TestProduct"/>/<see cref="TestProductCorpus"/> (which stay
/// byte-identical to the parallel Meilisearch test project's own copies) so every T-21–T-25 real-backend
/// test class declares the same field roles without repeating the builder chain five times.
/// </summary>
internal static class TestProductIndexDefinitions
{
    /// <summary>The standard field shape at the domain's default <c>MaxTotalHits</c>/<c>MaxFacetValues</c> ceilings.</summary>
    public static SearchIndexDefinition Standard(string indexName) =>
        Configure(new SearchIndexDefinitionBuilder(indexName)).Build().Value;

    /// <summary>The standard field shape with a caller-supplied <see cref="SearchIndexDefinition.MaxTotalHits"/> ceiling.</summary>
    public static SearchIndexDefinition WithMaxTotalHits(string indexName, int maxTotalHits) =>
        Configure(new SearchIndexDefinitionBuilder(indexName)).MaxTotalHits(maxTotalHits).Build().Value;

    /// <summary>
    /// The standard field shape with caller-supplied index-level text analysis — the neutral synonym and
    /// stop-word declarations.
    /// </summary>
    public static SearchIndexDefinition WithTextAnalysis(
        string indexName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> synonyms,
        IReadOnlyList<string> stopWords)
    {
        var builder = Configure(new SearchIndexDefinitionBuilder(indexName));
        foreach (var (term, replacements) in synonyms)
        {
            builder.Synonym(term, replacements.ToArray());
        }

        return builder.StopWords(stopWords.ToArray()).Build().Value;
    }

    /// <summary>The standard field shape with a caller-supplied <see cref="SearchIndexDefinition.MaxFacetValues"/> cap.</summary>
    public static SearchIndexDefinition WithMaxFacetValues(string indexName, int maxFacetValues) =>
        Configure(new SearchIndexDefinitionBuilder(indexName)).MaxFacetValues(maxFacetValues).Build().Value;

    private static SearchIndexDefinitionBuilder Configure(SearchIndexDefinitionBuilder builder) => builder
        .PrimaryKey(TestProductFields.DocumentId)
        .TenantField(TestProductFields.TenantId)
        .Field(TestProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
        .Field(TestProductFields.Name, SearchFieldKind.Text, searchable: true)
        .Field(TestProductFields.Description, SearchFieldKind.Text, searchable: true)
        .Field(TestProductFields.Status, SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field(TestProductFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true, sortable: true)
        .Field(TestProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true)
        .Field(TestProductFields.Stock, SearchFieldKind.Integer, filterable: true, sortable: true)
        .Field(TestProductFields.InStock, SearchFieldKind.Boolean, filterable: true)
        .Field(TestProductFields.CreatedAt, SearchFieldKind.DateTimeOffset, filterable: true, sortable: true);
}
