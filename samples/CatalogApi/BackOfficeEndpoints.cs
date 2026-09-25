using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Suggest;

namespace CatalogApi;

/// <summary>
/// The back office, served by ElasticSearch. The count and search endpoints are neutral; the
/// aggregation, cursor and suggest endpoints are ElasticSearch-exclusive and take a compile-time
/// dependency on that provider package.
/// </summary>
public static class BackOfficeEndpoints
{
    public static void MapBackOfficeEndpoints(this WebApplication app)
    {
        var backOffice = app.MapGroup("/back-office/{tenantId}").WithTags("Back office");

        // The same neutral contract the storefront uses, against a different engine and document type.
        backOffice.MapGet("/order-lines/count", async (
            TenantId tenantId,
            ISearchIndex<OrderLineDocument> index,
            string? region,
            CancellationToken ct) =>
        {
            SearchFilter? filter = region is null
                ? null
                : SearchFilter.Eq(OrderLineFields.Region, SearchValue.From(region));

            var count = await index.CountAsync(filter, TenantScope.For(tenantId), ct);
            return count.ToProblemDetailsResult(c => Results.Ok(new
            {
                value = c.Value,
                accuracy = c.Accuracy.ToString(),
                isExact = c.IsExact,
                display = c.ToString(),
            }));
        });

        // ── ElasticSearch-exclusive from here down ───────────────────────────────────────────────

        // Structured aggregations. Meilisearch offers facet counts and numeric min/max and nothing
        // else — the gap is absence, not degree, which is why this contract lives in the provider
        // package rather than being watered down into the neutral surface.
        backOffice.MapGet("/order-lines/revenue-by-region", async (
            TenantId tenantId,
            IAnalyticsSearch<OrderLineDocument> analytics,
            CancellationToken ct) =>
        {
            var aggregations = new[]
            {
                AggregationRequest.Terms(
                    "by_region",
                    OrderLineFields.Region,
                    size: 10,
                    subAggregations: [AggregationRequest.Stats("revenue", OrderLineFields.Revenue)]),
                AggregationRequest.Cardinality("distinct_categories", OrderLineFields.Category),
            };

            var result = await analytics.AggregateAsync(
                filter: null, aggregations, TenantScope.For(tenantId), ct);

            return result.ToProblemDetailsResult(set =>
            {
                var regions = set.TryGetTerms("by_region", out var terms)
                    ? terms.Buckets.Select(b => new { region = b.Key, orders = b.DocCount })
                    : [];

                var distinct = set.TryGetCardinality("distinct_categories", out var cardinality)
                    ? cardinality.Value
                    : 0;

                return Results.Ok(new { regions, distinctCategories = distinct });
            });
        });

        // Deep pagination past the MaxTotalHits ceiling, via point-in-time + search_after. Meilisearch
        // has no equivalent; its callers walk the corpus with EnumerateAsync instead.
        //
        // Two shapes ship. StreamAsync is the one most exports want — an IAsyncEnumerable that opens,
        // walks and closes the point-in-time for you. The open/read/close trio below exists for an
        // export that must checkpoint its cursor across process restarts, which IAsyncEnumerable cannot
        // express; this endpoint uses it because a stateless HTTP endpoint is exactly that case.
        backOffice.MapGet("/order-lines/cursor", async (
            TenantId tenantId,
            ICursorSearch<OrderLineDocument> cursor,
            int size,
            CancellationToken ct) =>
        {
            var keepAlive = TimeSpan.FromMinutes(1);
            var opened = await cursor.OpenCursorAsync(
                new SearchRequest { PageSize = size <= 0 ? 5 : size },
                TenantScope.For(tenantId),
                keepAlive,
                ct);

            if (opened.IsFailure)
            {
                return opened.ToProblemDetailsResult(_ => Results.Empty);
            }

            try
            {
                var page = await cursor.ReadCursorAsync(opened.Value, ct);
                return page.ToProblemDetailsResult(p => Results.Ok(new
                {
                    exhausted = p.IsExhausted,
                    items = p.Hits.Select(h => new
                    {
                        h.Document.DocumentId,
                        h.Document.ProductName,
                        h.Document.Revenue,
                    }),
                }));
            }
            finally
            {
                // Always release the point-in-time; leaking one pins segments on the cluster.
                await cursor.CloseCursorAsync(opened.Value, CancellationToken.None);
            }
        });

        // The streaming shape, for comparison — no cursor bookkeeping at the call site at all.
        backOffice.MapGet("/order-lines/stream", (
            TenantId tenantId,
            ICursorSearch<OrderLineDocument> cursor,
            CancellationToken ct) =>
        {
            async IAsyncEnumerable<object> WalkAsync()
            {
                await foreach (var hit in cursor.StreamAsync(
                    new SearchRequest { PageSize = 4 },
                    TenantScope.For(tenantId),
                    TimeSpan.FromMinutes(1),
                    ct))
                {
                    yield return new { hit.Document.DocumentId, hit.Document.ProductName };
                }
            }

            return Results.Ok(WalkAsync());
        });

        // The completion suggester. Returns suggestion STRINGS with weights, not documents — a
        // different data structure and a different result shape from Meilisearch's instant search,
        // which is exactly why neither was neutralised into a shared "type-ahead" contract.
        backOffice.MapGet("/order-lines/suggest", async (
            TenantId tenantId,
            ISuggestSearch<OrderLineDocument> suggest,
            string prefix,
            CancellationToken ct,
            bool fuzzy = false) =>
        {
            var suggestions = await suggest.SuggestAsync(
                Catalog.OrderLineSuggestField, prefix, TenantScope.For(tenantId), size: 5, fuzzy: fuzzy, ct);

            return suggestions.ToProblemDetailsResult(list => Results.Ok(
                list.Select(s => new { s.Text, s.DocumentId, s.Score })));
        });
    }
}
