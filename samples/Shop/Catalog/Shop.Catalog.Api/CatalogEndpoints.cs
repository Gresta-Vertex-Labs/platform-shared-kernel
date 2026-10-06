using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;
using Shop.Catalog.Application.Images;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Infrastructure.Pricing;

namespace Shop.Catalog.Api;

/// <summary>The new product's id.</summary>
public sealed record ProductCreatedResponse(Guid Id);

/// <summary>The body of a price change.</summary>
public sealed record ChangePriceRequest(decimal Price, string Currency);

/// <summary>Where the product image can be downloaded for the next few minutes.</summary>
public sealed record ImageUrlResponse(Uri Url);

/// <summary>The catalog's HTTP surface. Every route requires an authenticated caller; permissions are checked by the pipeline.</summary>
public sealed class CatalogEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var products = app.NewVersionedApi("Products")
            .MapGroup("/products")
            .HasApiVersion(1.0)
            .RequireAuthorization();

        products
            .MapPost(
                "/",
                (CreateProductCommand command, ISender sender, CancellationToken ct) =>
                    sender
                        .Send(command, ct)
                        .ToCreated(id => $"/products/{id}", id => new ProductCreatedResponse(id))
            )
            .WithName("CreateProduct");

        products
            .MapGet(
                "/{id:guid}",
                (Guid id, ISender sender, CancellationToken ct) =>
                    sender.Send(new GetProductQuery(id), ct).ToOk()
            )
            .WithName("GetProduct");

        products
            .MapPut(
                "/{id:guid}/price",
                (Guid id, ChangePriceRequest body, ISender sender, CancellationToken ct) =>
                    sender
                        .Send(new ChangePriceCommand(id, body.Price, body.Currency), ct)
                        .ToNoContent()
            )
            .WithName("ChangePrice");

        // The image is the raw request body; its Content-Type is stored with it.
        products
            .MapPut(
                "/{id:guid}/image",
                (Guid id, HttpRequest request, ISender sender, CancellationToken ct) =>
                    sender
                        .Send(
                            new UploadProductImageCommand(
                                id,
                                request.Body,
                                request.ContentType ?? "application/octet-stream"
                            ),
                            ct
                        )
                        .ToNoContent()
            )
            .WithName("UploadProductImage");

        products
            .MapGet(
                "/{id:guid}/image-url",
                (Guid id, ISender sender, CancellationToken ct) =>
                    sender
                        .Send(new GetProductImageUrlQuery(id), ct)
                        .ToOk(url => new ImageUrlResponse(url))
            )
            .WithName("GetProductImageUrl");

        products
            .MapPost(
                "/{id:guid}/description-suggestion",
                (Guid id, ISender sender, CancellationToken ct) =>
                    sender.Send(new SuggestDescriptionQuery(id), ct).ToOk()
            )
            .WithName("SuggestDescription");

        var search = app.NewVersionedApi("Search")
            .MapGroup("/search")
            .HasApiVersion(1.0)
            .RequireAuthorization();

        search
            .MapGet(
                "/",
                (string? q, string? category, int? page, ISender sender, CancellationToken ct) =>
                    sender.Send(new SearchProductsQuery(q, category, page ?? 1), ct).ToOk()
            )
            .WithName("SearchProducts");

        search
            .MapGet(
                "/semantic",
                (string q, ISender sender, CancellationToken ct) =>
                    sender.Send(new SemanticSearchQuery(q), ct).ToOk()
            )
            .WithName("SemanticSearch");

        search
            .MapGet(
                "/back-office",
                (string? q, ISender sender, CancellationToken ct) =>
                    sender.Send(new BackOfficeSearchQuery(q), ct).ToOk()
            )
            .WithName("BackOfficeSearch");

        // Operational view of what this replica heard on Redis Pub/Sub; used by the end-to-end tests.
        app.MapGet("/ops/price-changes", (PriceChangeLog log) => log.Heard).RequireAuthorization();
    }
}
