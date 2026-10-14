using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Caching;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Images;

/// <summary>Uploads a product image to the tenant's image store and attaches it to the product.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record UploadProductImageCommand(Guid ProductId, Stream Content, string ContentType)
    : ICommand<string>,
        IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
        [CacheKeyRef.For<GetProductQuery>(ProductId.ToString("D", CultureInfo.InvariantCulture))];
}

public sealed class UploadProductImageHandler(
    IRequestContext caller,
    IRepository<Product, ProductId> products,
    [FromKeyedServices(CatalogIndexes.ImageStore)] ITenantFileStorage images
) : ICommandHandler<UploadProductImageCommand, string>
{
    public async Task<Result<string>> Handle(
        UploadProductImageCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<string>.Failure(tenant.Error);
        }

        var product = await products.GetByIdAsync(
            new ProductId(command.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result<string>.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, command.ProductId)
            );
        }

        string key = ProductImageKeys.Main(product.Id);
        var uploaded = await images
            .ForTenant(tenant.Value)
            .UploadAsync(
                key,
                command.Content,
                new FileUploadOptions { ContentType = command.ContentType },
                cancellationToken
            );
        if (uploaded.IsFailure)
        {
            return Result<string>.Failure(uploaded.Error);
        }

        product.AttachImage(key);
        return Result<string>.Success(key);
    }
}

/// <summary>
/// A short-lived presigned <c>PUT</c> the browser uploads the product image to directly, so the file never passes
/// through the service. The client sends the returned headers with the upload, then confirms it with
/// <see cref="ConfirmProductImageUploadCommand"/>.
/// </summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record RequestProductImageUploadCommand(Guid ProductId, string ContentType)
    : ICommand<PresignedRequest>;

public sealed class RequestProductImageUploadHandler(
    IRequestContext caller,
    IReadRepository<Product, ProductId> products,
    [FromKeyedServices(CatalogIndexes.ImageStore)] ITenantFileStorage images
) : ICommandHandler<RequestProductImageUploadCommand, PresignedRequest>
{
    public async Task<Result<PresignedRequest>> Handle(
        RequestProductImageUploadCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<PresignedRequest>.Failure(tenant.Error);
        }

        var product = await products.GetByIdAsync(
            new ProductId(command.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result<PresignedRequest>.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, command.ProductId)
            );
        }

        return await images
            .ForTenant(tenant.Value)
            .CreateUploadUrlAsync(
                ProductImageKeys.Main(product.Id),
                new PresignedUploadOptions
                {
                    ContentType = command.ContentType,
                    Expiry = TimeSpan.FromMinutes(5),
                },
                cancellationToken
            );
    }
}

/// <summary>Attaches an image the browser uploaded with a presigned URL, once the object exists.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record ConfirmProductImageUploadCommand(Guid ProductId) : ICommand, IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
        [CacheKeyRef.For<GetProductQuery>(ProductId.ToString("D", CultureInfo.InvariantCulture))];
}

public sealed class ConfirmProductImageUploadHandler(
    IRequestContext caller,
    IRepository<Product, ProductId> products,
    [FromKeyedServices(CatalogIndexes.ImageStore)] ITenantFileStorage images
) : ICommandHandler<ConfirmProductImageUploadCommand>
{
    public async Task<Result> Handle(
        ConfirmProductImageUploadCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result.Failure(tenant.Error);
        }

        var product = await products.GetByIdAsync(
            new ProductId(command.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, command.ProductId)
            );
        }

        string key = ProductImageKeys.Main(product.Id);
        var exists = await images.ForTenant(tenant.Value).ExistsAsync(key, cancellationToken);
        if (exists.IsFailure)
        {
            return Result.Failure(exists.Error);
        }

        if (!exists.Value)
        {
            return Result.Failure(CatalogMessages.NoImage.ToError(ErrorType.NotFound));
        }

        product.AttachImage(key);
        return Result.Success();
    }
}

/// <summary>The object key of a product's image inside the tenant's image store.</summary>
public static class ProductImageKeys
{
    public static string Main(ProductId productId) => $"{productId.Value:D}/main";
}

/// <summary>A short-lived URL the browser downloads the product image from directly.</summary>
[RequirePermission(CatalogPermissions.Read)]
public sealed record GetProductImageUrlQuery(Guid ProductId) : IQuery<Uri>;

public sealed class GetProductImageUrlHandler(
    IRequestContext caller,
    IReadRepository<Product, ProductId> products,
    [FromKeyedServices(CatalogIndexes.ImageStore)] ITenantFileStorage images
) : IQueryHandler<GetProductImageUrlQuery, Uri>
{
    public async Task<Result<Uri>> Handle(
        GetProductImageUrlQuery query,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<Uri>.Failure(tenant.Error);
        }

        var product = await products.GetByIdAsync(
            new ProductId(query.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result<Uri>.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, query.ProductId)
            );
        }

        if (product.ImageKey is null)
        {
            return Result<Uri>.Failure(CatalogMessages.NoImage.ToError(ErrorType.NotFound));
        }

        var url = await images
            .ForTenant(tenant.Value)
            .CreateDownloadUrlAsync(
                product.ImageKey,
                new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(5) },
                cancellationToken
            );
        return url.IsFailure ? Result<Uri>.Failure(url.Error) : Result<Uri>.Success(url.Value.Url);
    }
}
