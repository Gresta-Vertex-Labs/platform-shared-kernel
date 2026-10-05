using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Querying;

namespace SharedKernel.Search.ElasticSearch.Diagnostics;

/// <summary>The ElasticSearch <see cref="ISearchProviderDescriptor"/> — singleton, zero I/O.</summary>
/// <remarks>
/// Deliberately enforces restrictions ElasticSearch itself does not impose — a filter/sort/facet
/// field absent from the registered <see cref="SearchIndexDefinition"/> is rejected before I/O, and
/// <c>Page * PageSize &gt; MaxTotalHits</c> is rejected even where <c>index.max_result_window</c>
/// would allow it. See the "Why MaxTotalHits defaults to 1000 on both providers" note in
/// <c>src/Infrastructure/Search/CLAUDE.md</c>.
/// </remarks>
internal sealed class ElasticSearchProviderDescriptor : ISearchProviderDescriptor
{
    private readonly IReadOnlyDictionary<string, SearchIndexDefinition> _indexDefinitions;

    /// <summary>Initializes a new <see cref="ElasticSearchProviderDescriptor"/>.</summary>
    public ElasticSearchProviderDescriptor(
        IReadOnlyDictionary<string, SearchIndexDefinition> indexDefinitions, int maxTotalHits, int maxFacetValues)
    {
        _indexDefinitions = indexDefinitions;
        MaxTotalHits = maxTotalHits;
        MaxFacetValues = maxFacetValues;
    }

    /// <inheritdoc />
    public string ProviderName => SearchWellKnown.ElasticSearchProviderName;

    /// <inheritdoc />
    public int MaxTotalHits { get; }

    /// <inheritdoc />
    public int MaxFacetValues { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> RegisteredIndexes => _indexDefinitions.Keys.ToArray();

    /// <inheritdoc />
    public Result Validate(string indexName, SearchRequest request)
    {
        if (!_indexDefinitions.TryGetValue(indexName, out var definition))
        {
            return Result.Failure(SearchErrors.IndexNotFound(indexName));
        }

        return ElasticSearchRequestValidator.Validate(definition, request);
    }
}
