namespace SharedKernel.Search.ElasticSearch.Suggest;

/// <summary>One completion suggestion returned by <see cref="ISuggestSearch{TDocument}"/>.</summary>
/// <remarks>
/// A suggestion is a <em>string</em> plus the weight the completion field was indexed with — not a
/// document. ElasticSearch's completion suggester answers from an FST built over the suggest field's
/// inputs, and the matching document is a separate lookup the caller makes by
/// <see cref="DocumentId"/> if it needs one. Modelling this as a document would imply a hydration this
/// engine deliberately does not perform, and would make the suggester as expensive as a search.
/// </remarks>
public sealed record SearchSuggestion
{
    /// <summary>Gets the suggested text.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the id of the document the suggestion was indexed from, so a caller can fetch the full
    /// document with <c>ISearchIndex&lt;TDocument&gt;.GetAsync</c> when the user picks it.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Gets the relevance score ElasticSearch assigned this suggestion — the indexed weight, adjusted
    /// for fuzziness when the query was fuzzy. Higher ranks first; the list is already ordered.
    /// </summary>
    public required double Score { get; init; }
}
