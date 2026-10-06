using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.BusinessRules;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace Shop.Catalog.Domain;

/// <summary>A product a tenant sells. The catalog's only aggregate.</summary>
public sealed class Product : TenantedAuditableAggregateRoot<ProductId>
{
    /// <summary>The longest name a product may have.</summary>
    public const int MaxNameLength = 200;

    private Product(
        ProductId id,
        TenantId tenantId,
        ProductDetails details,
        Money price,
        IClock clock
    )
        : base(id, tenantId, clock)
    {
        CheckRule(new ProductNameIsRequired(details.Name));
        CheckRule(new SkuIsWellFormed(details.Sku));
        CheckRule(new PriceIsPositive(price));

        Sku = details.Sku.Trim().ToUpperInvariant();
        Name = details.Name.Trim();
        Description = details.Description.Trim();
        Brand = details.Brand.Trim();
        Category = details.Category.Trim();
        Price = price;
        RaiseDomainEvent(at => new ProductCreated(id, tenantId) { OccurredOn = at });
    }

    /// <summary>ORM materialisation only.</summary>
    private Product() { }

    /// <summary>The stock-keeping unit, unique per tenant, upper case.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>The display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The marketing description.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The brand.</summary>
    public string Brand { get; private set; } = string.Empty;

    /// <summary>The category.</summary>
    public string Category { get; private set; } = string.Empty;

    /// <summary>The current price.</summary>
    public Money Price { get; private set; } = null!;

    /// <summary>The storage key of the product image in the <c>product-images</c> store, when one was uploaded.</summary>
    public string? ImageKey { get; private set; }

    /// <summary>Adds a product to a tenant's catalog.</summary>
    public static ValidationResult<Product> Create(
        TenantId tenantId,
        ProductDetails details,
        Money price,
        IClock clock
    ) => TryCreate(() => new Product(ProductId.New(), tenantId, details, price, clock));

    /// <summary>Changes the price, raising <see cref="ProductPriceChanged"/> when it actually changes.</summary>
    public Result ChangePrice(Money newPrice)
    {
        var rule = new PriceIsPositive(newPrice);
        if (rule.IsBroken())
        {
            return Result.Failure(Error.Validation(rule.Code, rule.Message));
        }

        if (newPrice == Price)
        {
            return Result.Success();
        }

        var oldPrice = Price;
        Price = newPrice;
        RaiseDomainEvent(at => new ProductPriceChanged(Id, TenantId, oldPrice, newPrice)
        {
            OccurredOn = at,
        });
        return Result.Success();
    }

    /// <summary>Records the storage key of the uploaded product image.</summary>
    public void AttachImage(string imageKey) => ImageKey = imageKey;
}

/// <summary>The descriptive fields of a new <see cref="Product"/>.</summary>
public sealed record ProductDetails(
    string Sku,
    string Name,
    string Description,
    string Brand,
    string Category
);
