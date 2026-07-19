using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Abstractions;

/// <summary>
/// A singleton, zero-I/O descriptor of the active search provider's identity and ceilings, plus a
/// zero-I/O pre-flight validator for <see cref="SearchRequest"/> shapes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type carries no capability-flags enum, deliberately:</b> an <c>if (caps.HasFlag(...))</c>
/// branch at an application call site is a platform violation — degradation in search is silent
/// wrongness, not a downgraded UX. The consumer-facing capability mechanism is the compile error a
/// provider swap produces against the provider-package-declared exclusive contracts, never a runtime
/// flag check.
/// </para>
/// <para>
/// <b><see cref="MaxTotalHits"/> is a readable property, not only a failure:</b> a BFF caps its pager
/// UI at <c>ceil(MaxTotalHits / pageSize)</c> up front instead of discovering the ceiling as a
/// pagination-limit-exceeded error on page 51.
/// </para>
/// <para>
/// <b><see cref="Validate"/> is the zero-I/O, zero-container pre-flight:</b> it runs exactly the same
/// legality checks the executor runs — pagination ceiling, field filterability/sortability/
/// facetability against the registered <see cref="SearchIndexDefinition"/>, facet-count cap,
/// structural request invariants — without touching the network. This is what makes "fail at
/// composition time, not query time" actionable: a consuming service can assert, in a plain unit test
/// with no container, that every <see cref="SearchRequest"/> shape it constructs is legal on the
/// configured provider.
/// </para>
/// </remarks>
public interface ISearchProviderDescriptor
{
    /// <summary>Gets the active provider's name.</summary>
    string ProviderName { get; }

    /// <summary>Gets the active provider's pagination ceiling (<c>Page * PageSize</c> upper bound).</summary>
    int MaxTotalHits { get; }

    /// <summary>Gets the active provider's per-facet value-count cap.</summary>
    int MaxFacetValues { get; }

    /// <summary>Gets the names of every index registered against this provider.</summary>
    IReadOnlyList<string> RegisteredIndexes { get; }

    /// <summary>
    /// Validates <paramref name="request"/> against the index named <paramref name="indexName"/>'s
    /// registered <see cref="SearchIndexDefinition"/> — the same checks the executor runs, with zero
    /// I/O.
    /// </summary>
    Result Validate(string indexName, SearchRequest request);
}
