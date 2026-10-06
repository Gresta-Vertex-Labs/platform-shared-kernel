using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Tenancy;

namespace Shop.Catalog.Domain;

/// <summary>Raised when a product is added to a tenant's catalog.</summary>
[DomainEventVersion(1)]
public sealed record ProductCreated(ProductId ProductId, TenantId TenantId) : DomainEvent;

/// <summary>Raised when a product's price changes.</summary>
[DomainEventVersion(1)]
public sealed record ProductPriceChanged(
    ProductId ProductId,
    TenantId TenantId,
    Money OldPrice,
    Money NewPrice
) : DomainEvent;
