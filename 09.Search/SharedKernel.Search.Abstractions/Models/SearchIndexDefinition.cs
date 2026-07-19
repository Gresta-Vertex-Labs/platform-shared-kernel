using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The neutral declaration of one search index — its fields, their roles, its optional tenant field,
/// and its pagination/facet ceilings.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the "thin abstraction" premise is unavoidably compromised: Meilisearch
/// rejects a filter on any attribute absent from <c>filterableAttributes</c> and a sort on any
/// attribute absent from <c>sortableAttributes</c>, both being index settings applied ahead of time,
/// while ElasticSearch filters any mapped field at query time. If field roles were provider-private,
/// the identical <see cref="SearchRequest"/> would succeed on ElasticSearch and 400 on Meilisearch.
/// This type is the neutral declaration that makes a neutral query legal on both engines.
/// </para>
/// <para>
/// <see cref="TenantField"/> lives here, not on provider options — a service may legitimately have
/// one tenanted index and one global one. Per-index placement is what lets the fail-closed
/// tenant-scope-missing guard on <c>ISearchIndex&lt;TDocument&gt;</c> arm itself exactly where it
/// should, and nowhere else.
/// </para>
/// </remarks>
public sealed record SearchIndexDefinition
{
    /// <summary>Gets the index name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the primary-key field name.</summary>
    public string PrimaryKeyField { get; init; } = SearchWellKnown.DefaultPrimaryKeyField;

    /// <summary>
    /// Gets the tenant discriminator field name, or <see langword="null"/> for a single-tenant/global
    /// index.
    /// </summary>
    public string? TenantField { get; init; }

    /// <summary>Gets the declared fields.</summary>
    public IReadOnlyList<SearchFieldDefinition> Fields { get; init; } = [];

    /// <summary>Gets the pagination ceiling (<c>Page * PageSize</c> must not exceed this value).</summary>
    public int MaxTotalHits { get; init; } = SearchWellKnown.DefaultMaxTotalHits;

    /// <summary>Gets the per-facet value-count cap.</summary>
    public int MaxFacetValues { get; init; } = SearchWellKnown.DefaultMaxFacetValues;

    /// <summary>
    /// Creates a <see cref="SearchIndexDefinition"/> with the given <paramref name="name"/> and
    /// <paramref name="fields"/>, using default values for every other member. For full control over
    /// <see cref="PrimaryKeyField"/>, <see cref="TenantField"/>, <see cref="MaxTotalHits"/>, and
    /// <see cref="MaxFacetValues"/>, use <see cref="SearchIndexDefinitionBuilder"/> instead.
    /// </summary>
    /// <returns>
    /// A failed <see cref="Result{T}"/> with <see cref="SearchErrors.InvalidIndexDefinition"/> when
    /// <paramref name="name"/> is empty, any field name is empty, or two fields share a name;
    /// otherwise a successful <see cref="Result{T}"/>.
    /// </returns>
    public static Result<SearchIndexDefinition> Create(string name, IReadOnlyList<SearchFieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<SearchIndexDefinition>.Failure(
                SearchErrors.InvalidIndexDefinition("Index name must not be empty."));
        }

        var fieldValidation = ValidateFields(fields);
        if (fieldValidation is not null)
        {
            return Result<SearchIndexDefinition>.Failure(fieldValidation);
        }

        return Result<SearchIndexDefinition>.Success(new SearchIndexDefinition
        {
            Name = name,
            Fields = fields,
        });
    }

    /// <summary>
    /// Validates that no <paramref name="fields"/> entry has an empty name and that no two entries
    /// share a name. Returns <see langword="null"/> when valid, or the
    /// <see cref="SearchErrors.InvalidIndexDefinition"/> <see cref="Primitives.Errors.Error"/> when
    /// not. Shared by <see cref="Create"/> and <see cref="SearchIndexDefinitionBuilder.Build"/> so the
    /// rule lives in exactly one place.
    /// </summary>
    internal static Primitives.Errors.Error? ValidateFields(IReadOnlyList<SearchFieldDefinition> fields)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                return SearchErrors.InvalidIndexDefinition("Field name must not be empty.");
            }

            if (!seenNames.Add(field.Name))
            {
                return SearchErrors.InvalidIndexDefinition($"Duplicate field name '{field.Name}'.");
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a stable fingerprint of this index definition's schema — a SHA-256 hash, rendered as
    /// lowercase hex, over a deterministic canonical string built from <see cref="Name"/>,
    /// <see cref="PrimaryKeyField"/>, <see cref="TenantField"/>, <see cref="MaxTotalHits"/>,
    /// <see cref="MaxFacetValues"/>, and every <see cref="Fields"/> entry sorted by
    /// <see cref="SearchFieldDefinition.Name"/> using <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    /// <remarks>
    /// Ordinal sorting makes the value independent of declaration order; rendering
    /// <see cref="SearchFieldDefinition.Kind"/> as an explicit <see cref="int"/> makes it independent
    /// of enum member renames. Written into provisioned-index metadata so
    /// <c>ISearchIndexProvisioner.ProbeAsync</c> can detect schema drift.
    /// </remarks>
    public string Fingerprint
    {
        get
        {
            var canonical = BuildCanonicalString();
            var bytes = Encoding.UTF8.GetBytes(canonical);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexStringLower(hash);
        }
    }

    private string BuildCanonicalString()
    {
        var builder = new StringBuilder();
        builder.Append(Name).Append('\n');
        builder.Append(PrimaryKeyField).Append('\n');
        builder.Append(TenantField ?? string.Empty).Append('\n');
        builder.Append(MaxTotalHits.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(MaxFacetValues.ToString(CultureInfo.InvariantCulture)).Append('\n');

        foreach (var field in Fields.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            builder
                .Append(field.Name).Append('|')
                .Append((int)field.Kind).Append('|')
                .Append(field.Searchable ? '1' : '0').Append('|')
                .Append(field.Filterable ? '1' : '0').Append('|')
                .Append(field.Sortable ? '1' : '0').Append('|')
                .Append(field.Facetable ? '1' : '0')
                .Append('\n');
        }

        return builder.ToString();
    }
}
