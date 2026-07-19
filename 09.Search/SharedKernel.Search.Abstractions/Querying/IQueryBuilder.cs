using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Querying;

/// <summary>
/// A fluent, immutable builder for a <see cref="SearchRequest"/> — optional sugar over constructing
/// a <see cref="SearchRequest"/> directly.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// <b>Immutable — enforced, not merely asserted:</b> every method returns a <em>new</em>
/// <see cref="SearchQueryBuilder{TDocument}"/> instance. A partially-built query may be safely shared,
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
/// </remarks>
public interface IQueryBuilder<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Sets the free-text query.</summary>
    IQueryBuilder<TDocument> Matching(string? freeText);

    /// <summary>Sets whether every free-text term must match (AND) rather than any (OR).</summary>
    IQueryBuilder<TDocument> MatchAllTerms(bool matchAll);

    /// <summary>Restricts which fields free text is matched against.</summary>
    IQueryBuilder<TDocument> SearchingIn(params string[] fields);

    /// <summary>
    /// Adds a filter predicate. Repeated calls AND together — they never replace a prior call.
    /// </summary>
    IQueryBuilder<TDocument> Where(SearchFilter filter);

    /// <summary>Adds an ascending sort on <paramref name="field"/>.</summary>
    IQueryBuilder<TDocument> OrderBy(string field);

    /// <summary>Adds a descending sort on <paramref name="field"/>.</summary>
    IQueryBuilder<TDocument> OrderByDescending(string field);

    /// <summary>Sets the 1-based page number and page size.</summary>
    IQueryBuilder<TDocument> Page(int page, int pageSize);

    /// <summary>Requires an exact <see cref="SearchResults{TDocument}.TotalHits"/>.</summary>
    IQueryBuilder<TDocument> RequireExactTotalHits();

    /// <summary>Requests facet value/count distributions for <paramref name="facetFields"/>.</summary>
    IQueryBuilder<TDocument> Faceting(params string[] facetFields);

    /// <summary>Requests numeric min/max facet statistics for <paramref name="facetFields"/>.</summary>
    IQueryBuilder<TDocument> WithNumericFacetStats(params string[] facetFields);

    /// <summary>Sets the highlight request.</summary>
    IQueryBuilder<TDocument> Highlighting(HighlightRequest highlight);

    /// <summary>Restricts which fields are returned on each hit.</summary>
    IQueryBuilder<TDocument> Returning(params string[] fields);

    /// <summary>Builds the <see cref="SearchRequest"/>, validating provider-independent invariants.</summary>
    Result<SearchRequest> Build();
}
