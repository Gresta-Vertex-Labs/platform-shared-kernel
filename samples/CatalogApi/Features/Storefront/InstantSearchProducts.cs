using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Instant;

namespace CatalogApi.Features.Storefront;

/// <summary>
/// Prefix/type-ahead search. Meilisearch-exclusive: <see cref="IInstantSearch{TDocument}"/> takes a compile-time
/// dependency on SharedKernel.Search.Meilisearch, so swapping this service to ElasticSearch turns this handler into a
/// build error naming itself — the point of declaring an engine's exclusive capabilities in its own package.
/// </summary>
public sealed record InstantSearchProducts(string TenantId, string Text) : IQuery<SearchResults<ProductDocument>>;

public sealed class InstantSearchProductsHandler(IInstantSearch<ProductDocument> instant)
    : IQueryHandler<InstantSearchProducts, SearchResults<ProductDocument>>
{
    public Task<Result<SearchResults<ProductDocument>>> Handle(InstantSearchProducts query, CancellationToken cancellationToken) =>
        instant.InstantAsync(new InstantSearchRequest { FreeText = query.Text, Limit = 5 }, TenantScope.Of(query.TenantId), cancellationToken);
}
