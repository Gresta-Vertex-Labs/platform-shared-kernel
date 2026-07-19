using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Querying;

namespace SharedKernel.Search.Meilisearch.Diagnostics;

/// <summary>The Meilisearch <see cref="ISearchProviderDescriptor"/> — singleton, zero I/O.</summary>
internal sealed class MeilisearchProviderDescriptor : ISearchProviderDescriptor
{
    private readonly IReadOnlyDictionary<string, SearchIndexDefinition> _indexDefinitions;

    /// <summary>Initializes a new <see cref="MeilisearchProviderDescriptor"/>.</summary>
    public MeilisearchProviderDescriptor(
        IReadOnlyDictionary<string, SearchIndexDefinition> indexDefinitions, int maxTotalHits, int maxFacetValues)
    {
        _indexDefinitions = indexDefinitions;
        MaxTotalHits = maxTotalHits;
        MaxFacetValues = maxFacetValues;
    }

    /// <inheritdoc />
    public string ProviderName => SearchWellKnown.MeilisearchProviderName;

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

        return MeilisearchRequestValidator.Validate(definition, request);
    }

    /// <summary>Attempts to retrieve the registered <see cref="SearchIndexDefinition"/> for <paramref name="indexName"/>.</summary>
    internal bool TryGetDefinition(string indexName, out SearchIndexDefinition definition)
    {
        if (_indexDefinitions.TryGetValue(indexName, out var found))
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }
}
