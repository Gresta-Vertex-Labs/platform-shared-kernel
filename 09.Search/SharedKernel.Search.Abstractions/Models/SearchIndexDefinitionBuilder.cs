using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// A fluent builder for <see cref="SearchIndexDefinition"/> — the ergonomic entry point providers'
/// <c>AddIndex&lt;TDocument&gt;</c> DI extensions expose to a composition root.
/// </summary>
public sealed class SearchIndexDefinitionBuilder
{
    private readonly string _name;
    private readonly List<SearchFieldDefinition> _fields = [];
    private string? _primaryKeyField;
    private string? _tenantField;
    private readonly Dictionary<string, IReadOnlyList<string>> _synonyms = new(StringComparer.Ordinal);
    private readonly List<string> _stopWords = [];
    private int? _maxTotalHits;
    private int? _maxFacetValues;

    /// <summary>Initializes a new <see cref="SearchIndexDefinitionBuilder"/> for the given index name.</summary>
    public SearchIndexDefinitionBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>Sets the primary-key field name.</summary>
    public SearchIndexDefinitionBuilder PrimaryKey(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _primaryKeyField = field;
        return this;
    }

    /// <summary>Sets the tenant discriminator field name.</summary>
    public SearchIndexDefinitionBuilder TenantField(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _tenantField = field;
        return this;
    }

    /// <summary>Declares a field.</summary>
    public SearchIndexDefinitionBuilder Field(
        string name,
        SearchFieldKind kind,
        bool searchable = false,
        bool filterable = false,
        bool sortable = false,
        bool facetable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _fields.Add(new SearchFieldDefinition
        {
            Name = name,
            Kind = kind,
            Searchable = searchable,
            Filterable = filterable,
            Sortable = sortable,
            Facetable = facetable,
        });
        return this;
    }

    /// <summary>Sets the pagination ceiling.</summary>
    public SearchIndexDefinitionBuilder MaxTotalHits(int value)
    {
        _maxTotalHits = value;
        return this;
    }

    /// <summary>Sets the per-facet value-count cap.</summary>
    public SearchIndexDefinitionBuilder MaxFacetValues(int value)
    {
        _maxFacetValues = value;
        return this;
    }

    /// <summary>
    /// Declares a one-way synonym mapping: a query containing <paramref name="term"/> also matches
    /// <paramref name="replacements"/>. Declare the reverse direction with a second call when you want
    /// symmetry — see <see cref="SearchIndexDefinition.Synonyms"/> for why one-way is the portable
    /// shape.
    /// </summary>
    /// <remarks>Calling this twice with the same <paramref name="term"/> replaces the earlier mapping.</remarks>
    public SearchIndexDefinitionBuilder Synonym(string term, params string[] replacements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        ArgumentNullException.ThrowIfNull(replacements);
        _synonyms[term] = replacements.ToArray();
        return this;
    }

    /// <summary>
    /// Declares stop words removed from free-text queries and indexed content across every searchable
    /// field. Repeated calls accumulate. Supply the words already lowercased.
    /// </summary>
    public SearchIndexDefinitionBuilder StopWords(params string[] stopWords)
    {
        ArgumentNullException.ThrowIfNull(stopWords);
        _stopWords.AddRange(stopWords);
        return this;
    }

    /// <summary>Builds the <see cref="SearchIndexDefinition"/>.</summary>
    /// <returns>
    /// A failed <see cref="Result{T}"/> with <see cref="SearchErrors.InvalidIndexDefinition"/> for a
    /// duplicate field name, an empty field name, or a <see cref="TenantField"/> naming a field that
    /// is not declared <see cref="SearchFieldDefinition.Filterable"/>; otherwise a successful
    /// <see cref="Result{T}"/>.
    /// </returns>
    public Result<SearchIndexDefinition> Build()
    {
        var fieldValidation = SearchIndexDefinition.ValidateFields(_fields);
        if (fieldValidation is not null)
        {
            return Result<SearchIndexDefinition>.Failure(fieldValidation);
        }

        var textAnalysisValidation = SearchIndexDefinition.ValidateTextAnalysis(_synonyms, _stopWords);
        if (textAnalysisValidation is not null)
        {
            return Result<SearchIndexDefinition>.Failure(textAnalysisValidation);
        }

        if (_tenantField is not null)
        {
            var tenantField = _fields.Find(f => string.Equals(f.Name, _tenantField, StringComparison.Ordinal));
            if (tenantField is null || !tenantField.Filterable)
            {
                return Result<SearchIndexDefinition>.Failure(
                    SearchErrors.InvalidIndexDefinition(
                        $"TenantField '{_tenantField}' must name a field declared Filterable."));
            }
        }

        return Result<SearchIndexDefinition>.Success(new SearchIndexDefinition
        {
            Name = _name,
            PrimaryKeyField = _primaryKeyField ?? SearchWellKnown.DefaultPrimaryKeyField,
            TenantField = _tenantField,
            Fields = _fields.ToArray(),
            MaxTotalHits = _maxTotalHits ?? SearchWellKnown.DefaultMaxTotalHits,
            MaxFacetValues = _maxFacetValues ?? SearchWellKnown.DefaultMaxFacetValues,
            Synonyms = new Dictionary<string, IReadOnlyList<string>>(_synonyms, StringComparer.Ordinal),
            StopWords = _stopWords.ToArray(),
        });
    }
}
