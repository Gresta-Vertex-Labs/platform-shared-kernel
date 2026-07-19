using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.Search.Abstractions.Querying;

/// <summary>The static entry point for building a <see cref="Models.SearchRequest"/>.</summary>
public static class SearchQuery
{
    /// <summary>Starts a new <see cref="IQueryBuilder{TDocument}"/> for <typeparamref name="TDocument"/>.</summary>
    /// <typeparam name="TDocument">The search document type.</typeparam>
    public static IQueryBuilder<TDocument> For<TDocument>()
        where TDocument : class, ISearchDocument
        => new SearchQueryBuilder<TDocument>();
}
