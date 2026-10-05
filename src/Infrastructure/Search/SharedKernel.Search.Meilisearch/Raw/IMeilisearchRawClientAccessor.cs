using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Tenancy;

namespace SharedKernel.Search.Meilisearch.Raw;

/// <summary>
/// The last-resort escape hatch onto the raw Meilisearch SDK client — registered only when the
/// composition root calls <c>.AllowRawClientAccess()</c>, which logs a startup warning.
/// </summary>
/// <remarks>
/// <para>
/// The genuine last resort for capabilities the four typed contracts over neutral models cannot reach
/// (e.g. hybrid/vector search, federated multi-search). The primary provider-exclusive mechanism is
/// <see cref="IInstantSearch{TDocument}"/> and <see cref="ITenantSearchTokenIssuer"/> — reach for this
/// only when those are insufficient.
/// </para>
/// <para>
/// <b>THE HATCH BYPASSES TENANT SCOPING.</b> <c>TenantScope</c> injection happens inside the neutral
/// filter compiler; a raw client call receives none of it. A multi-tenant service using this accessor
/// MUST apply its own tenant predicate.
/// </para>
/// </remarks>
public interface IMeilisearchRawClientAccessor
{
    /// <summary>Gets the raw Meilisearch client.</summary>
    global::Meilisearch.MeilisearchClient Client { get; }

    /// <summary>Gets a raw Meilisearch <see cref="global::Meilisearch.Index"/> handle for <paramref name="indexName"/>.</summary>
    global::Meilisearch.Index IndexHandle(string indexName);
}

/// <summary>The default implementation of <see cref="IMeilisearchRawClientAccessor"/>.</summary>
internal sealed class MeilisearchRawClientAccessor : IMeilisearchRawClientAccessor
{
    /// <summary>Initializes a new <see cref="MeilisearchRawClientAccessor"/>.</summary>
    public MeilisearchRawClientAccessor(global::Meilisearch.MeilisearchClient client)
    {
        Client = client;
    }

    /// <inheritdoc />
    public global::Meilisearch.MeilisearchClient Client { get; }

    /// <inheritdoc />
    public global::Meilisearch.Index IndexHandle(string indexName) => Client.Index(indexName);
}
