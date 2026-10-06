using FluentValidation;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.Specifications;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Products;

/// <summary>Adds a product to the caller's catalog and indexes it everywhere.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string Description,
    string Brand,
    string Category,
    decimal Price,
    string Currency
) : ICommand<Guid>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(c => c.Sku).NotEmpty();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Product.MaxNameLength);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Price).GreaterThan(0);
    }
}

public sealed class CreateProductHandler(
    IRequestContext caller,
    IRepository<Product, ProductId> products,
    ProductIndexer indexer,
    IClock clock
) : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<Guid>.Failure(tenant.Error);
        }

        var currency = Currency.Create(command.Currency);
        if (!currency.IsValid)
        {
            return Result<Guid>.Failure(Error.Validation(currency.Errors));
        }

        var price = Money.Create(command.Price, currency.Value);
        if (!price.IsValid)
        {
            return Result<Guid>.Failure(Error.Validation(price.Errors));
        }

        var created = Product.Create(
            tenant.Value,
            new ProductDetails(
                command.Sku,
                command.Name,
                command.Description,
                command.Brand,
                command.Category
            ),
            price.Value,
            clock
        );
        if (!created.IsValid)
        {
            return Result<Guid>.Failure(Error.Validation(created.Errors));
        }

        // Read through the tenant filter: a SKU is unique per tenant, and another tenant's SKU is invisible here.
        string sku = created.Value.Sku;
        if (
            await products.AnyAsync(
                Specification<Product>.Create(p => p.Sku == sku),
                cancellationToken
            )
        )
        {
            return Result<Guid>.Failure(CatalogMessages.SkuTaken.ToError(ErrorType.Conflict, sku));
        }

        await products.AddAsync(created.Value, cancellationToken);

        var indexed = await indexer.IndexAsync(created.Value, cancellationToken);
        return indexed.IsFailure
            ? Result<Guid>.Failure(indexed.Error)
            : Result<Guid>.Success(created.Value.Id.Value);
    }
}
