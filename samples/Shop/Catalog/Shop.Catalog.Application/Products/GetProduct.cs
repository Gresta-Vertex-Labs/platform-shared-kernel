using System.Globalization;
using OpenFeature;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Caching;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.FeatureManagement;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Products;

/// <summary>A product as the storefront shows it.</summary>
/// <param name="ServedAt">When the handler built this answer; a cache hit returns the original time.</param>
/// <param name="Badge"><c>"new"</c> when the <see cref="CatalogFeatures.NewArrivalBadge"/> flag is on for the caller.</param>
public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    string Brand,
    string Category,
    decimal Price,
    string Currency,
    bool HasImage,
    string? Badge,
    DateTimeOffset ServedAt
);

/// <summary>Reads one product of the caller's catalog. Cached per tenant; a price change evicts it.</summary>
[RequirePermission(CatalogPermissions.Read)]
public sealed record GetProductQuery(Guid ProductId) : ICacheableQuery<ProductDto>
{
    public CachePolicy CachePolicy =>
        CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

    public string CacheKey => ProductId.ToString("D", CultureInfo.InvariantCulture);
}

public sealed class GetProductHandler(
    IReadRepository<Product, ProductId> products,
    IFeatureClient features,
    IClock clock
) : IQueryHandler<GetProductQuery, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(
        GetProductQuery query,
        CancellationToken cancellationToken
    )
    {
        var product = await products.GetByIdAsync(
            new ProductId(query.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result<ProductDto>.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, query.ProductId)
            );
        }

        bool badged = await features.IsEnabledAsync(
            CatalogFeatures.NewArrivalBadge,
            cancellationToken
        );

        return Result<ProductDto>.Success(
            new ProductDto(
                product.Id.Value,
                product.Sku,
                product.Name,
                product.Description,
                product.Brand,
                product.Category,
                product.Price.Amount,
                product.Price.Currency.Code,
                product.ImageKey is not null,
                badged ? "new" : null,
                clock.UtcNow
            )
        );
    }
}
