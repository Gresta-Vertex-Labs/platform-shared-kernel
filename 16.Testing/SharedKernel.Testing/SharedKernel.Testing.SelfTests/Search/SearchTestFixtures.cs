using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.Testing.SelfTests.Search;

/// <summary>
/// Minimal <see cref="ISearchDocument"/> implementation shared by the <c>Search/</c> self-tests —
/// proves <c>Search/InMemorySearchIndex&lt;TDocument&gt;</c>/
/// <c>Search/InMemorySearchIndexProvisioner</c>/<c>Search/InMemorySearchProviderDescriptor</c>
/// against <c>SharedKernel.Search.Abstractions</c>'s documented contract. No consuming domain has
/// adopted these fakes yet (see <c>16.Testing/state-map.md</c> T-50/T-51), so these self-tests are
/// the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
internal sealed record TestProductDocument : ISearchDocument
{
    /// <inheritdoc />
    public required string DocumentId { get; init; }

    /// <summary>Gets the product name (a <c>Text</c>/searchable field in the test index definitions).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the product status (a <c>Keyword</c>/filterable+facetable field).</summary>
    public required string Status { get; init; }

    /// <summary>Gets the product price (a <c>Decimal</c>/filterable+sortable+facetable field).</summary>
    public required double Price { get; init; }

    /// <summary>Gets the owning tenant discriminator value.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Gets the creation timestamp (a <c>DateTimeOffset</c>/filterable+sortable field).</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
