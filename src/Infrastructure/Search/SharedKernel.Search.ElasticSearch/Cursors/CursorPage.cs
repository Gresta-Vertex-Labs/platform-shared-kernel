using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Cursors;

/// <summary>One page of results read from an open <see cref="SearchCursor"/>.</summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
public sealed record CursorPage<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>Gets the hits on this page, in relevance order.</summary>
    public required IReadOnlyList<SearchHit<TDocument>> Hits { get; init; }

    /// <summary>Gets the cursor to read the next page with, or <see langword="null"/> when exhausted.</summary>
    public SearchCursor? NextCursor { get; init; }

    /// <summary>Gets a value indicating whether this was the final page (<see cref="NextCursor"/> is <see langword="null"/>).</summary>
    public bool IsExhausted => NextCursor is null;
}
