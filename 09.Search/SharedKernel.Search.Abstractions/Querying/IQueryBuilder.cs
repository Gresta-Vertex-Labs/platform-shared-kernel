using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Querying;

/// <summary>
/// A fluent, immutable builder for a <see cref="SearchRequest"/> — optional sugar over constructing
/// a <see cref="SearchRequest"/> directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable — enforced, not merely asserted:</b> every method returns a <em>new</em>
/// <see cref="SearchQueryBuilder"/> instance. A partially-built query may be safely shared,
/// cached, fanned out, or used as a template.
/// </para>
/// <para>
/// <b>Repeated <see cref="Where"/> calls AND together — the single most important ergonomic decision
/// here:</b> the alternative, last-call-wins, is exactly the silent-clause-dropping defect class that
/// leaks tenant data. For OR, compose explicitly:
/// <c>Where(SearchFilter.Any(a, b))</c>.
/// </para>
/// <para><see cref="Page"/> is 1-based, matching <c>PagedList&lt;T&gt;.Page</c>.</para>
/// <para>
/// <b><see cref="Build"/> validates only provider-independent invariants</b> — page/pageSize bounds,
/// empty field names, duplicate sort fields, an empty highlight field list. It cannot validate
/// filterability, sortability, or the pagination ceiling — those are engine-configuration facts,
/// checked by <c>ISearchProviderDescriptor.Validate</c> and, ultimately, by the executor.
/// </para>
/// <para>
/// <b>No expression trees, no <c>IQueryable</c>:</b> fields are strings, not
/// <c>Expression&lt;Func&lt;TDocument, object&gt;&gt;</c>. An <c>IQueryable</c> surface promises a
/// completeness no search engine delivers and lands its failures at runtime.
/// </para>
/// <para>
/// <b>Non-generic, and it must stay that way.</b> This type carried a <c>TDocument</c> parameter until
/// the pre-publish pass, which read as type safety and delivered none: because fields are strings by
/// the deliberate decision above, <c>TDocument</c> appeared in no parameter, no field, and nowhere in
/// the built <see cref="SearchRequest"/> — it only parameterised the fluent return types. It was a
/// phantom parameter that forced callers to name a type for nothing and emitted a separate generic
/// instantiation of all twelve methods per document type. Do not reintroduce it: a type parameter that
/// constrains nothing advertises a guarantee this builder cannot make. The document type enters at
/// execution, on <c>ISearchIndex&lt;TDocument&gt;.SearchAsync</c>, where it is genuinely load-bearing.
/// </para>
/// </remarks>
public interface IQueryBuilder
{
    /// <summary>Sets the free-text query.</summary>
    IQueryBuilder Matching(string? freeText);

    /// <summary>Sets whether every free-text term must match (AND) rather than any (OR).</summary>
    IQueryBuilder MatchAllTerms(bool matchAll);

    /// <summary>Restricts which fields free text is matched against.</summary>
    IQueryBuilder SearchingIn(params string[] fields);

    /// <summary>
    /// Adds a filter predicate. Repeated calls AND together — they never replace a prior call.
    /// </summary>
    IQueryBuilder Where(SearchFilter filter);

    /// <summary>Adds an ascending sort on <paramref name="field"/>.</summary>
    IQueryBuilder OrderBy(string field);

    /// <summary>Adds a descending sort on <paramref name="field"/>.</summary>
    IQueryBuilder OrderByDescending(string field);

    /// <summary>Sets the 1-based page number and page size.</summary>
    IQueryBuilder Page(int page, int pageSize);

    /// <summary>Requires an exact <c>SearchResults&lt;TDocument&gt;.TotalHits</c>.</summary>
    IQueryBuilder RequireExactTotalHits();

    /// <summary>Requests facet value/count distributions for <paramref name="facetFields"/>.</summary>
    IQueryBuilder Faceting(params string[] facetFields);

    /// <summary>Requests numeric min/max facet statistics for <paramref name="facetFields"/>.</summary>
    IQueryBuilder WithNumericFacetStats(params string[] facetFields);

    /// <summary>Sets the highlight request.</summary>
    IQueryBuilder Highlighting(HighlightRequest highlight);

    /// <summary>Restricts which fields are returned on each hit.</summary>
    IQueryBuilder Returning(params string[] fields);

    /// <summary>Builds the <see cref="SearchRequest"/>, validating provider-independent invariants.</summary>
    Result<SearchRequest> Build();
}
