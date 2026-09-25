using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.ElasticSearch.Suggest;

/// <summary>
/// The ElasticSearch-exclusive completion-suggester contract — declared here, not in
/// <c>SharedKernel.Search.Abstractions</c>, so referencing it takes a compile-time dependency on this
/// package.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// <b>Why this is not neutral, and why it is not the same thing as Meilisearch's instant search.</b>
/// ElasticSearch's completion suggester is a purpose-built in-memory FST (finite state transducer)
/// structure, populated from a dedicated <c>completion</c>-typed mapping field at index time, that
/// answers prefix queries in roughly constant time and returns <em>suggestion strings with weights</em>
/// rather than documents. Meilisearch has no such structure and no such field type: its type-ahead
/// story is ordinary prefix matching over the regular index, which returns documents. The two solve
/// the same product problem with different data structures, different index-time requirements, and
/// different result shapes — a neutral <c>SuggestAsync</c> would have to pick one shape and emulate it
/// badly on the other engine.
/// </para>
/// <para>
/// This is the deliberate mirror of <c>IInstantSearch&lt;TDocument&gt;</c> in the Meilisearch package:
/// each engine's own type-ahead primitive is exposed faithfully, in its own package, so a service that
/// uses one and then swaps providers gets a build error naming every call site rather than a silent
/// change in behaviour.
/// </para>
/// <para>
/// <b>Requires an index-time declaration.</b> A completion field is not a query-time option — it must
/// exist in the mapping before any document is indexed. Declare it with
/// <c>ElasticSearchBuilder.WithCompletionField(indexName, suggestField, sourceField)</c>; the
/// provisioner then adds it to the mapping, and re-indexing is required for existing documents to
/// become suggestable.
/// </para>
/// <para>
/// <b><see cref="TenantScope"/> is mandatory and non-defaulted</b>, exactly as on the neutral read
/// path: suggestions are derived from document content, so an unscoped suggester leaks one tenant's
/// product names, customer names or order references to another as the caller types.
/// </para>
/// </remarks>
public interface ISuggestSearch<TDocument>
    where TDocument : class, ISearchDocument
{
    /// <summary>
    /// Returns completion suggestions for <paramref name="prefix"/> from the completion field
    /// <paramref name="suggestField"/>, scoped to <paramref name="tenantScope"/>.
    /// </summary>
    /// <param name="suggestField">
    /// The completion-typed field to suggest from, as declared via
    /// <c>ElasticSearchBuilder.WithCompletionField</c>.
    /// </param>
    /// <param name="prefix">The text typed so far. An empty or whitespace prefix is rejected.</param>
    /// <param name="tenantScope">
    /// The tenant to restrict suggestions to — mandatory, and rejected as
    /// <see cref="Abstractions.Errors.SearchErrors.TenantScopeMissing"/> when the index declares a
    /// tenant field and <see cref="TenantScope.None"/> is passed.
    /// </param>
    /// <param name="size">The maximum number of suggestions to return.</param>
    /// <param name="fuzzy">
    /// Whether to tolerate typos in <paramref name="prefix"/>. ElasticSearch applies a length-scaled
    /// edit distance; it costs materially more than an exact prefix walk, so it is opt-in.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<IReadOnlyList<SearchSuggestion>>> SuggestAsync(
        string suggestField,
        string prefix,
        TenantScope tenantScope,
        int size = 10,
        bool fuzzy = false,
        CancellationToken cancellationToken = default);
}
