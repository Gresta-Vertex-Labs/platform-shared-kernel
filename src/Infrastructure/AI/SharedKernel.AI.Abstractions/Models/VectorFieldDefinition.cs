namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The declaration of one metadata field on a <see cref="VectorCollectionDefinition"/> — its scalar
/// kind and whether it is filterable.
/// </summary>
/// <remarks>
/// <para>
/// <b>One boolean, deliberately, not four like <c>09.Search</c>'s <c>SearchFieldDefinition</c>:</b>
/// there is no Searchable (no full-text concept on vector metadata), no Sortable (similarity query
/// results are always ordered by score, and <c>ScrollAsync</c>'s order is unspecified by design), no
/// Facetable (faceting is a full-text-search concept absent from both vector engines' actual feature
/// sets). A field must be <see cref="Filterable"/> to appear in a <see cref="VectorFilter"/> against
/// it — this is what drives payload-index creation on Qdrant and scalar-index + nullable wiring on
/// Milvus at collection-provisioning time.
/// </para>
/// <para>
/// <b>This type, alongside <see cref="VectorCollectionDefinition"/>, is the surface to guard hardest
/// in review:</b> a request for a fifth boolean is very likely a claim about one engine that does not
/// hold for the other and must clear the seam rule explicitly before it lands.
/// </para>
/// </remarks>
public sealed record VectorFieldDefinition
{
    /// <summary>Gets the field name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the field's declared scalar kind.</summary>
    public required VectorFieldKind Kind { get; init; }

    /// <summary>Gets a value indicating whether this field may appear in a <see cref="VectorFilter"/>.</summary>
    public bool Filterable { get; init; }
}
