using System.Globalization;
using FluentValidation;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Caching;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Domain.Monetary;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Catalog.Application.Search;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Products;

/// <summary>Changes a product's price, re-indexes it and evicts its cached read after the commit.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record ChangePriceCommand(Guid ProductId, decimal Price, string Currency)
    : ICommand,
        IInvalidatesCache
{
    public IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate =>
        [CacheKeyRef.For<GetProductQuery>(ProductId.ToString("D", CultureInfo.InvariantCulture))];
}

public sealed class ChangePriceCommandValidator : AbstractValidator<ChangePriceCommand>
{
    public ChangePriceCommandValidator()
    {
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Price).GreaterThan(0);
    }
}

public sealed class ChangePriceHandler(
    IRepository<Product, ProductId> products,
    ProductIndexer indexer
) : ICommandHandler<ChangePriceCommand>
{
    public async Task<Result> Handle(
        ChangePriceCommand command,
        CancellationToken cancellationToken
    )
    {
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

        var currency = Currency.Create(command.Currency);
        if (!currency.IsValid)
        {
            return Result.Failure(Error.Validation(currency.Errors));
        }

        var price = Money.Create(command.Price, currency.Value);
        if (!price.IsValid)
        {
            return Result.Failure(Error.Validation(price.Errors));
        }

        var changed = product.ChangePrice(price.Value);
        if (changed.IsFailure)
        {
            return changed;
        }

        return await indexer.IndexAsync(product, cancellationToken);
    }
}

/// <summary>A price change other catalog replicas and services hear about.</summary>
public sealed record PriceChange(Guid ProductId, Guid TenantId, decimal Price, string Currency);

/// <summary>Broadcasts price changes to every replica (Redis Pub/Sub in production).</summary>
public interface IPriceChangeBroadcaster
{
    /// <summary>Publishes the change; delivery is fire-and-forget.</summary>
    Task PublishAsync(PriceChange change, CancellationToken cancellationToken);
}

/// <summary>Broadcasts every <see cref="ProductPriceChanged"/> domain event.</summary>
public sealed class BroadcastPriceChange(IPriceChangeBroadcaster broadcaster)
    : IDomainEventHandler<ProductPriceChanged>
{
    public Task Handle(ProductPriceChanged domainEvent, CancellationToken cancellationToken) =>
        broadcaster.PublishAsync(
            new PriceChange(
                domainEvent.ProductId.Value,
                domainEvent.TenantId.Value,
                domainEvent.NewPrice.Amount,
                domainEvent.NewPrice.Currency.Code
            ),
            cancellationToken
        );
}
