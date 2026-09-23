using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Querying;
using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Tenancy;

namespace CatalogApi;

/// <summary>
/// The storefront, served by Meilisearch. Everything here except the last two endpoints is written
/// against the neutral contracts only, so it would work unchanged on ElasticSearch.
/// </summary>
/// <remarks>
/// Every search verb returns a <c>Result</c>, and each endpoint maps it to a typed result with <c>ToOk(…)</c>: an
/// unreachable engine is <c>search.unreachable</c> (503), a timeout <c>search.timeout</c> (504), a bad query a 400 —
/// all RFC 9457 problems, none of them an exception.
/// </remarks>
public static class StorefrontEndpoints
{
    public static void MapStorefrontEndpoints(this WebApplication app)
    {
        var storefront = app.MapGroup("/storefront/{tenantId}").WithTags("Storefront");

        // Free text + filters + sort + facets + highlighting + paging, all through the neutral builder.
        storefront.MapGet("/products", (
            string tenantId,
            ISearchIndex<ProductDocument> index,
            string? q,
            string? category,
            double? maxPrice,
            bool? inStockOnly,
            int page = 1,
            int pageSize = 5,
            CancellationToken ct = default) =>
        {
            var filters = new List<SearchFilter>();
            if (category is not null)
            {
                filters.Add(SearchFilter.Eq(ProductFields.Category, SearchValue.From(category)));
            }

            if (maxPrice is { } ceiling)
            {
                filters.Add(SearchFilter.Between(ProductFields.Price, from: null, to: SearchValue.From(ceiling)));
            }

            if (inStockOnly == true)
            {
                filters.Add(SearchFilter.Eq(ProductFields.InStock, SearchValue.From(true)));
            }

            var query = SearchQuery.New()
                .Matching(q)
                .Page(page, pageSize)
                .Faceting(ProductFields.Category, ProductFields.Brand)
                .Highlighting(new HighlightRequest { Fields = [ProductFields.Name, ProductFields.Description] });

            // Repeated Where(...) calls AND together — they never replace a prior call.
            foreach (var filter in filters)
            {
                query = query.Where(filter);
            }

            // A malformed query fails Build() and never reaches the engine; one past this index's ceilings fails
            // SearchAsync's pre-flight check, also before any I/O. Either way the client gets the 400.
            return query.Build()
                .Bind(request => index.SearchAsync(request, TenantScope.Of(tenantId), ct))
                .ToOk(r => new
                {
                    total = r.TotalHits,
                    accuracy = r.Accuracy.ToString(),
                    page = r.Page,
                    pageSize = r.PageSize,
                    durationMs = r.Duration.TotalMilliseconds,
                    facets = r.Facets.ToDictionary(
                        f => f.Key,
                        f => f.Value.Values.Select(v => new { value = v.Value, count = v.Count })),
                    hits = r.Hits.Select(h => new
                    {
                        rank = h.Rank,
                        h.Document.DocumentId,
                        h.Document.Name,
                        h.Document.Brand,
                        h.Document.Category,
                        h.Document.Price,
                        h.Document.InStock,
                        highlights = h.Highlights,
                    }),
                });
        });

        // The headline of the pre-publish pass: a count now says how much it can be trusted.
        storefront.MapGet("/products/count", (
            string tenantId,
            ISearchIndex<ProductDocument> index,
            string? category,
            CancellationToken ct) =>
        {
            SearchFilter? filter = category is null
                ? null
                : SearchFilter.Eq(ProductFields.Category, SearchValue.From(category));

            return index.CountAsync(filter, TenantScope.Of(tenantId), ct).ToOk(c => new
            {
                value = c.Value,
                accuracy = c.Accuracy.ToString(),
                isExact = c.IsExact,
                display = c.ToString(),
            });
        });

        // Tenant-checked: a get-by-id for another tenant's document is NotFound, not a leak.
        storefront.MapGet("/products/{documentId}", (
            string tenantId,
            string documentId,
            ISearchIndex<ProductDocument> index,
            CancellationToken ct) =>
            index.GetAsync(documentId, TenantScope.Of(tenantId), ct).ToOk());

        // The corpus walk. Not Result-wrapped — the domain's one documented exception to the
        // Result-first rule, following the 06.Persistence/08.Storage streaming precedent.
        storefront.MapGet("/products/export", (
            string tenantId,
            ISearchIndex<ProductDocument> index,
            CancellationToken ct) =>
        {
            async IAsyncEnumerable<object> WalkAsync()
            {
                await foreach (var product in index.EnumerateAsync(
                    filter: null, TenantScope.Of(tenantId), batchSize: 4, ct))
                {
                    yield return new { product.DocumentId, product.Name, product.Price };
                }
            }

            return TypedResults.Ok(WalkAsync());
        });

        // ── Meilisearch-exclusive from here down ─────────────────────────────────────────────────
        //
        // Referencing IInstantSearch<T> or ITenantSearchTokenIssuer takes a compile-time dependency on
        // SharedKernel.Search.Meilisearch. Swap this service to ElasticSearch and these two endpoints
        // become build errors naming themselves — which is the entire point of declaring an engine's
        // exclusive capabilities in its own package rather than behind a runtime capability flag.

        storefront.MapGet("/products/instant", (
            string tenantId,
            IInstantSearch<ProductDocument> instant,
            string q,
            CancellationToken ct) =>
            instant.InstantAsync(new InstantSearchRequest { FreeText = q, Limit = 5 }, TenantScope.Of(tenantId), ct)
                .ToOk(r => r.Hits.Select(h => new { h.Document.DocumentId, h.Document.Name })));

        // A signed, expiring token a browser holds. The tenant filter inside it is enforced by the
        // ENGINE, not by this service — so a compromised front end still cannot read another tenant.
        storefront.MapPost("/products/search-token", (
            string tenantId,
            ITenantSearchTokenIssuer issuer,
            CancellationToken ct) =>
            issuer.IssueAsync(
                    TenantScope.Of(tenantId),
                    ProductFields.TenantId,
                    [Catalog.ProductsIndex],
                    TimeSpan.FromMinutes(5),
                    ct)
                .ToOk(t => new
                {
                    token = t.Value,
                    expiresAt = t.ExpiresAt,
                    scopedIndexes = t.ScopedIndexes,
                }));
    }
}
