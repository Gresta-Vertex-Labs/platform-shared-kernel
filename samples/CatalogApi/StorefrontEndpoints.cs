using CatalogApi.Features.Storefront;
using MediatR;
using SharedKernel.Presentation.WebApi;

namespace CatalogApi;

/// <summary>
/// The storefront, served by Meilisearch. Every handler behind it except the last two is written against the neutral
/// contracts only, so it would work unchanged on ElasticSearch.
/// </summary>
/// <remarks>
/// Each endpoint sends a query or command (<c>Features/Storefront</c>). Every search verb returns a <c>Result</c>, and
/// each endpoint maps it to a typed result with <c>ToOk(…)</c>: an unreachable engine is <c>search.unreachable</c>
/// (503), a timeout <c>search.timeout</c> (504), a bad query a 400 — all RFC 9457 problems, none of them an exception.
/// </remarks>
public sealed class StorefrontEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var storefront = app.MapGroup("/storefront/{tenantId}").WithTags("Storefront");

        storefront.MapGet("/products", (
            string tenantId,
            ISender sender,
            string? q,
            string? category,
            double? maxPrice,
            bool? inStockOnly,
            int page = 1,
            int pageSize = 5,
            CancellationToken ct = default) =>
            sender.Send(new SearchProducts(tenantId, q, category, maxPrice, inStockOnly, page, pageSize), ct)
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
                }));

        // The headline of the pre-publish pass: a count now says how much it can be trusted.
        storefront.MapGet("/products/count", (string tenantId, string? category, ISender sender, CancellationToken ct) =>
            sender.Send(new CountProducts(tenantId, category), ct).ToOk(c => new
            {
                value = c.Value,
                accuracy = c.Accuracy.ToString(),
                isExact = c.IsExact,
                display = c.ToString(),
            }));

        storefront.MapGet("/products/{documentId}", (string tenantId, string documentId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetProduct(tenantId, documentId), ct).ToOk());

        // The corpus walk, streamed as it is read: a stream query, not a Result (see ExportProducts).
        storefront.MapGet("/products/export", (string tenantId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(sender.CreateStream(new ExportProducts(tenantId), ct)));

        // ── Meilisearch-exclusive from here down (see the handlers) ─────────────────────────────────

        storefront.MapGet("/products/instant", (string tenantId, string q, ISender sender, CancellationToken ct) =>
            sender.Send(new InstantSearchProducts(tenantId, q), ct)
                .ToOk(r => r.Hits.Select(h => new { h.Document.DocumentId, h.Document.Name })));

        storefront.MapPost("/products/search-token", (string tenantId, ISender sender, CancellationToken ct) =>
            sender.Send(new IssueSearchToken(tenantId), ct)
                .ToOk(t => new
                {
                    token = t.Value,
                    expiresAt = t.ExpiresAt,
                    scopedIndexes = t.ScopedIndexes,
                }));
    }
}
