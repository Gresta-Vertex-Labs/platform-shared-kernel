using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.WebApi.Results;
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
public static class StorefrontEndpoints
{
    public static void MapStorefrontEndpoints(this WebApplication app)
    {
        var storefront = app.MapGroup("/storefront/{tenantId}").WithTags("Storefront");

        // Free text + filters + sort + facets + highlighting + paging, all through the neutral builder.
        storefront.MapGet("/products", async (
            TenantId tenantId,
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

            var request = query.Build();
            if (request.IsFailure)
            {
                return request.ToProblemDetailsResult(_ => Results.Empty);
            }

            var results = await index.SearchAsync(request.Value, TenantScope.For(tenantId), ct);
            return results.ToProblemDetailsResult(r => Results.Ok(new
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
            }));
        });

        // The headline of the pre-publish pass: a count now says how much it can be trusted.
        storefront.MapGet("/products/count", async (
            TenantId tenantId,
            ISearchIndex<ProductDocument> index,
            string? category,
            CancellationToken ct) =>
        {
            SearchFilter? filter = category is null
                ? null
                : SearchFilter.Eq(ProductFields.Category, SearchValue.From(category));

            var count = await index.CountAsync(filter, TenantScope.For(tenantId), ct);
            return count.ToProblemDetailsResult(c => Results.Ok(new
            {
                value = c.Value,
                accuracy = c.Accuracy.ToString(),
                isExact = c.IsExact,
                display = c.ToString(),
            }));
        });

        // Tenant-checked: a get-by-id for another tenant's document is NotFound, not a leak.
        storefront.MapGet("/products/{documentId}", async (
            TenantId tenantId,
            string documentId,
            ISearchIndex<ProductDocument> index,
            CancellationToken ct) =>
        {
            var product = await index.GetAsync(documentId, TenantScope.For(tenantId), ct);
            return product.ToProblemDetailsResult(p => Results.Ok(p));
        });

        // The corpus walk. Not Result-wrapped — the domain's one documented exception to the
        // Result-first rule, following the 06.Persistence/08.Storage streaming precedent.
        storefront.MapGet("/products/export", (
            TenantId tenantId,
            ISearchIndex<ProductDocument> index,
            CancellationToken ct) =>
        {
            async IAsyncEnumerable<object> WalkAsync()
            {
                await foreach (var product in index.EnumerateAsync(
                    filter: null, TenantScope.For(tenantId), batchSize: 4, ct))
                {
                    yield return new { product.DocumentId, product.Name, product.Price };
                }
            }

            return Results.Ok(WalkAsync());
        });

        // ── Meilisearch-exclusive from here down ─────────────────────────────────────────────────
        //
        // Referencing IInstantSearch<T> or ITenantSearchTokenIssuer takes a compile-time dependency on
        // SharedKernel.Search.Meilisearch. Swap this service to ElasticSearch and these two endpoints
        // become build errors naming themselves — which is the entire point of declaring an engine's
        // exclusive capabilities in its own package rather than behind a runtime capability flag.

        storefront.MapGet("/products/instant", async (
            TenantId tenantId,
            IInstantSearch<ProductDocument> instant,
            string q,
            CancellationToken ct) =>
        {
            var results = await instant.InstantAsync(
                new InstantSearchRequest { FreeText = q, Limit = 5 }, TenantScope.For(tenantId), ct);

            return results.ToProblemDetailsResult(r => Results.Ok(
                r.Hits.Select(h => new { h.Document.DocumentId, h.Document.Name })));
        });

        // A signed, expiring token a browser holds. The tenant filter inside it is enforced by the
        // ENGINE, not by this service — so a compromised front end still cannot read another tenant.
        storefront.MapPost("/products/search-token", async (
            TenantId tenantId,
            ITenantSearchTokenIssuer issuer,
            CancellationToken ct) =>
        {
            var token = await issuer.IssueAsync(
                TenantScope.For(tenantId),
                ProductFields.TenantId,
                [Catalog.ProductsIndex],
                TimeSpan.FromMinutes(5),
                ct);

            return token.ToProblemDetailsResult(t => Results.Ok(new
            {
                token = t.Value,
                expiresAt = t.ExpiresAt,
                scopedIndexes = t.ScopedIndexes,
            }));
        });
    }
}
