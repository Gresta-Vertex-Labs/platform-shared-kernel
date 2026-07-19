using System.Collections.Concurrent;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Testing.Search;

/// <summary>
/// In-memory test double for <see cref="ISearchProviderDescriptor"/>. Simulates the zero-I/O
/// provider-identity/ceiling surface and pre-flight <see cref="SearchRequest"/> validation, without
/// a real engine behind it.
/// </summary>
/// <remarks>
/// <para>
/// Keeps its own independent <see cref="SearchIndexDefinition"/> map, populated via
/// <see cref="RegisterIndex"/> -- it is NOT read through <see cref="InMemorySearchIndexProvisioner"/>
/// or any <see cref="InMemorySearchIndex{TDocument}"/> instance. See
/// <see cref="InMemorySearchIndex{TDocument}"/>'s remarks for the full deliberate non-coupling
/// rationale shared by all three <c>Search/</c> fakes. A consuming test that wants two of these
/// fakes to agree on one <see cref="SearchIndexDefinition"/> passes the same definition instance to
/// each explicitly.
/// </para>
/// <para>
/// <see cref="Validate"/> runs the exact same zero-I/O pre-flight pipeline as
/// <see cref="InMemorySearchIndex{TDocument}.SearchAsync"/>'s fail-loud validation step (field-role
/// checks, then the pagination ceiling) against a registered definition.
/// </para>
/// </remarks>
public sealed class InMemorySearchProviderDescriptor : ISearchProviderDescriptor
{
    private readonly ConcurrentDictionary<string, SearchIndexDefinition> _registeredIndexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new <see cref="InMemorySearchProviderDescriptor"/>.
    /// </summary>
    /// <param name="providerName">
    /// The provider name to report. Defaults to <c>"in-memory-fake"</c> -- deliberately neither
    /// <see cref="SearchWellKnown.MeilisearchProviderName"/> nor
    /// <see cref="SearchWellKnown.ElasticSearchProviderName"/>, so a consumer's own
    /// provider-name-branching or logging code cannot mistake this fake for a real engine.
    /// </param>
    public InMemorySearchProviderDescriptor(string providerName = "in-memory-fake")
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ProviderName = providerName;
    }

    /// <inheritdoc />
    public string ProviderName { get; set; }

    /// <inheritdoc cref="ISearchProviderDescriptor.MaxTotalHits" />
    public int MaxTotalHits { get; set; } = SearchWellKnown.DefaultMaxTotalHits;

    /// <inheritdoc cref="ISearchProviderDescriptor.MaxFacetValues" />
    public int MaxFacetValues { get; set; } = SearchWellKnown.DefaultMaxFacetValues;

    /// <inheritdoc />
    public IReadOnlyList<string> RegisteredIndexes => _registeredIndexes.Keys.ToArray();

    /// <summary>
    /// Registers <paramref name="definition"/> under <paramref name="indexName"/> -- a test-setup
    /// helper populating both <see cref="RegisteredIndexes"/> and the field-role knowledge
    /// <see cref="Validate"/> needs.
    /// </summary>
    public void RegisterIndex(string indexName, SearchIndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(indexName);
        ArgumentNullException.ThrowIfNull(definition);
        _registeredIndexes[indexName] = definition;
    }

    /// <inheritdoc />
    public Result Validate(string indexName, SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(indexName);
        ArgumentNullException.ThrowIfNull(request);

        if (!_registeredIndexes.TryGetValue(indexName, out var definition))
        {
            return Result.Failure(SearchErrors.IndexNotFound(indexName));
        }

        foreach (var sort in request.Sort)
        {
            if (!HasFieldRole(definition, sort.Field, static f => f.Sortable))
            {
                return Result.Failure(SearchErrors.FieldNotSortable(indexName, sort.Field));
            }
        }

        if (request.Filter is not null)
        {
            var filterError = ValidateFilterFieldRoles(definition, indexName, request.Filter);
            if (filterError is not null)
            {
                return Result.Failure(filterError);
            }
        }

        foreach (var facetField in request.Facets)
        {
            if (!HasFieldRole(definition, facetField, static f => f.Facetable))
            {
                return Result.Failure(SearchErrors.FieldNotFacetable(indexName, facetField));
            }
        }

        foreach (var statsField in request.NumericFacetStats)
        {
            if (!HasFieldRole(definition, statsField, static f => f.Facetable))
            {
                return Result.Failure(SearchErrors.FieldNotFacetable(indexName, statsField));
            }
        }

        if ((long)request.Page * request.PageSize > MaxTotalHits)
        {
            return Result.Failure(SearchErrors.PaginationLimitExceeded(request.Page, request.PageSize, MaxTotalHits, ProviderName));
        }

        return Result.Success();
    }

    /// <summary>Clears every registered index definition.</summary>
    public void Reset() => _registeredIndexes.Clear();

    private static bool HasFieldRole(SearchIndexDefinition definition, string field, Func<SearchFieldDefinition, bool> roleSelector) =>
        definition.Fields.Any(f => string.Equals(f.Name, field, StringComparison.Ordinal) && roleSelector(f));

    private static SharedKernel.Primitives.Errors.Error? ValidateFilterFieldRoles(
        SearchIndexDefinition definition, string indexName, SearchFilter filter) => filter switch
    {
        EqualFilter f => HasFieldRole(definition, f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(indexName, f.Field),
        NotEqualFilter f => HasFieldRole(definition, f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(indexName, f.Field),
        InFilter f => HasFieldRole(definition, f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(indexName, f.Field),
        RangeFilter f => HasFieldRole(definition, f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(indexName, f.Field),
        ExistsFilter f => HasFieldRole(definition, f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(indexName, f.Field),
        AndFilter f => f.Operands.Select(op => ValidateFilterFieldRoles(definition, indexName, op)).FirstOrDefault(static e => e is not null),
        OrFilter f => f.Operands.Select(op => ValidateFilterFieldRoles(definition, indexName, op)).FirstOrDefault(static e => e is not null),
        NotFilter f => ValidateFilterFieldRoles(definition, indexName, f.Operand),
    };
}
