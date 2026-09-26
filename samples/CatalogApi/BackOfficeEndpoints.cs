using CatalogApi.Features.BackOffice;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Presentation.WebApi;

namespace CatalogApi;

/// <summary>
/// The back office, served by ElasticSearch. The count is neutral; the aggregation, cursor and suggest handlers are
/// ElasticSearch-exclusive and take a compile-time dependency on that provider package (<c>Features/BackOffice</c>).
/// </summary>
public sealed class BackOfficeEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var backOffice = app.MapGroup("/back-office/{tenantId}").WithTags("Back office");

        backOffice.MapGet("/order-lines/count", (TenantId tenantId, string? region, ISender sender, CancellationToken ct) =>
            sender.Send(new CountOrderLines(tenantId, region), ct).ToOk(c => new
            {
                value = c.Value,
                accuracy = c.Accuracy.ToString(),
                isExact = c.IsExact,
                display = c.ToString(),
            }));

        // ── ElasticSearch-exclusive from here down ───────────────────────────────────────────────

        backOffice.MapGet("/order-lines/revenue-by-region", (TenantId tenantId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetRevenueByRegion(tenantId), ct).ToOk(set =>
            {
                var regions = set.TryGetTerms(GetRevenueByRegionHandler.ByRegion, out var terms)
                    ? terms.Buckets.Select(b => new { region = b.Key, orders = b.DocCount })
                    : [];

                var distinct = set.TryGetCardinality(GetRevenueByRegionHandler.DistinctCategories, out var cardinality)
                    ? cardinality.Value
                    : 0;

                return new { regions, distinctCategories = distinct };
            }));

        backOffice.MapGet("/order-lines/cursor", (TenantId tenantId, int size, ISender sender, CancellationToken ct) =>
            sender.Send(new ReadOrderLineCursor(tenantId, size), ct)
                .ToOk(p => new
                {
                    exhausted = p.IsExhausted,
                    items = p.Hits.Select(h => new
                    {
                        h.Document.DocumentId,
                        h.Document.ProductName,
                        h.Document.Revenue,
                    }),
                }));

        // The streaming shape: a stream query, not a Result (see StreamOrderLines).
        backOffice.MapGet("/order-lines/stream", (TenantId tenantId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(sender.CreateStream(new StreamOrderLines(tenantId), ct)));

        backOffice.MapGet("/order-lines/suggest", (
            TenantId tenantId,
            string prefix,
            ISender sender,
            CancellationToken ct,
            bool fuzzy = false) =>
            sender.Send(new SuggestOrderLines(tenantId, prefix, fuzzy), ct)
                .ToOk(list => list.Select(s => new { s.Text, s.DocumentId, s.Score })));
    }
}
