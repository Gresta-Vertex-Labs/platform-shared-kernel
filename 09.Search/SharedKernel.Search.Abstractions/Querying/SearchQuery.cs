namespace SharedKernel.Search.Abstractions.Querying;

/// <summary>The static entry point for building a <see cref="Models.SearchRequest"/>.</summary>
public static class SearchQuery
{
    /// <summary>Starts a new <see cref="IQueryBuilder"/>.</summary>
    /// <remarks>
    /// Takes no type parameter: a <see cref="Models.SearchRequest"/> is document-type-independent, and
    /// the builder that produces one has nothing to do with <c>TDocument</c> — see
    /// <see cref="IQueryBuilder"/>'s own remarks for why the parameter it used to carry was removed.
    /// The document type enters at execution, on <c>ISearchIndex&lt;TDocument&gt;.SearchAsync</c>.
    /// </remarks>
    public static IQueryBuilder New() => new SearchQueryBuilder();
}
