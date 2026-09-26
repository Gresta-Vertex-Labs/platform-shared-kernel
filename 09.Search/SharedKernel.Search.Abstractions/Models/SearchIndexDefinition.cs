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
    /// Gets the one-way synonym mappings applied to free-text matching across every
    /// <see cref="SearchFieldDefinition.Searchable"/> field, keyed by the term a query may contain and
    /// valued by the terms it should additionally match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One-way by design, because that is the shape both engines share exactly.</b> Meilisearch's
    /// <c>synonyms</c> setting is natively one-way per key — declaring <c>"tv" =&gt; ["television"]</c>
    /// makes a search for <c>tv</c> also match <c>television</c> and <em>not</em> the reverse.
    /// ElasticSearch's two-way <c>synonym_graph</c> equivalence syntax (<c>"tv, television"</c>) has no
    /// Meilisearch counterpart, so this platform emits ElasticSearch's explicit-mapping form
    /// (<c>"tv =&gt; tv, television"</c>) instead, which is one-way and therefore byte-for-byte
    /// equivalent to what Meilisearch does. Declare both directions explicitly when you want symmetry —
    /// the asymmetry is visible in your own configuration rather than differing silently per engine.
    /// </para>
    /// <para>
    /// <b>Index-level, never field-level.</b> This is not the analyzer/tokenizer/normalizer knob
    /// <see cref="SearchFieldDefinition"/> permanently refuses — that refusal stands. A per-field
    /// analyzer is where a neutral mapping DSL lies, because the two engines' analysis chains do not
    /// correspond. A whole-index synonym list and stop-word list do correspond, exactly, and are
    /// applied by both providers to every searchable field uniformly.
    /// </para>
    /// <para>
    /// <b>Applied at provisioning time and fingerprinted.</b> ElasticSearch cannot change an index's
    /// analysis settings on a live index, so — like every other part of this definition —
    /// <c>ISearchIndexProvisioner.EnsureIndexAsync</c> applies these when it creates the index and
    /// reports a conflict rather than rewriting them afterwards. Changing a synonym or stop-word list
    /// is a staging-index rebuild plus a cutover, and the change moves <see cref="Fingerprint"/> so
    /// the index's readiness probe detects a deployment that forgot to rebuild.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the stop words removed from free-text queries and from indexed content across every
    /// <see cref="SearchFieldDefinition.Searchable"/> field.
    /// </summary>
    /// <remarks>
    /// Meilisearch's <c>stopWords</c> setting and ElasticSearch's <c>stop</c> token filter have the
    /// same observable effect — the listed terms stop contributing to matching — so this is a genuinely
    /// neutral declaration. Supply the words already lowercased: both providers lowercase before the
    /// stop filter runs, so a capitalised entry simply never matches anything. Same provisioning and
    /// fingerprinting rules as <see cref="Synonyms"/>.
    /// </remarks>
    public IReadOnlyList<string> StopWords { get; init; } = [];

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
    /// Validates that no <paramref name="synonyms"/> key or replacement and no
    /// <paramref name="stopWords"/> entry is blank, and that no replacement list is empty. Returns
    /// <see langword="null"/> when valid, or the <see cref="SearchErrors.InvalidIndexDefinition"/>
    /// <see cref="Primitives.Errors.Error"/> when not. Shared by <see cref="Create"/> and
    /// <see cref="SearchIndexDefinitionBuilder.Build"/> so the rule lives in exactly one place.
    /// </summary>
    /// <remarks>
    /// A blank term is rejected rather than trimmed away: both engines accept it, neither can ever
    /// match it, and silently dropping it would hide a configuration typo behind a synonym list that
    /// simply does not work.
    /// </remarks>
    internal static Primitives.Errors.Error? ValidateTextAnalysis(
        IReadOnlyDictionary<string, IReadOnlyList<string>> synonyms,
        IReadOnlyList<string> stopWords)
    {
        foreach (var (term, replacements) in synonyms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return SearchErrors.InvalidIndexDefinition("A synonym term must not be empty.");
            }

            if (replacements.Count == 0)
            {
                return SearchErrors.InvalidIndexDefinition(
                    $"Synonym term '{term}' must declare at least one replacement.");
            }

            if (replacements.Any(string.IsNullOrWhiteSpace))
            {
                return SearchErrors.InvalidIndexDefinition(
                    $"Synonym term '{term}' declares an empty replacement.");
            }
        }

        if (stopWords.Any(string.IsNullOrWhiteSpace))
        {
            return SearchErrors.InvalidIndexDefinition("A stop word must not be empty.");
        }

        return null;
    }

    /// <summary>
    /// Computes a stable fingerprint of this index definition's schema — a SHA-256 hash, rendered as
    /// lowercase hex, over a deterministic canonical string built from <see cref="Name"/>,
    /// <see cref="PrimaryKeyField"/>, <see cref="TenantField"/>, <see cref="MaxTotalHits"/>,
    /// <see cref="MaxFacetValues"/>, every <see cref="Fields"/> entry sorted by
    /// <see cref="SearchFieldDefinition.Name"/>, every <see cref="Synonyms"/> entry sorted by key, and
    /// <see cref="StopWords"/> sorted — all using <see cref="StringComparer.Ordinal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordinal sorting makes the value independent of declaration order; rendering
    /// <see cref="SearchFieldDefinition.Kind"/> as an explicit <see cref="int"/> makes it independent
    /// of enum member renames. Written into provisioned-index metadata so
    /// the index's readiness probe can detect schema drift — including a synonym or
    /// stop-word list edited without the staging rebuild those settings require.
    /// </para>
    /// <para>
    /// <b>A method, not a property, deliberately:</b> it allocates a canonical string and runs SHA-256
    /// on every call, which is far more than a caller may reasonably assume a property getter costs.
    /// Caching it in a field is not an option either — this is a <see langword="record"/>, so a
    /// <c>with</c> expression copies private fields verbatim and would carry a stale hash onto a
    /// modified definition. Callers that need it more than once hold the returned string.
    /// </para>
    /// </remarks>
    public string ComputeFingerprint()
    {
        var canonical = BuildCanonicalString();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
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

        foreach (var synonym in Synonyms.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            builder.Append(synonym.Key).Append("=>");
            foreach (var replacement in synonym.Value.OrderBy(v => v, StringComparer.Ordinal))
            {
                builder.Append(replacement).Append(',');
            }

            builder.Append('\n');
        }

        foreach (var stopWord in StopWords.OrderBy(w => w, StringComparer.Ordinal))
        {
            builder.Append(stopWord).Append('\n');
        }

        return builder.ToString();
    }
}
