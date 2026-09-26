using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Storefront;

/// <summary>Tenant-checked: a get-by-id for another tenant's document is NotFound, not a leak.</summary>
public sealed record GetProduct(TenantId TenantId, string DocumentId) : IQuery<ProductDocument>;

public sealed class GetProductHandler(ISearchIndex<ProductDocument> index) : IQueryHandler<GetProduct, ProductDocument>
{
    public Task<Result<ProductDocument>> Handle(GetProduct query, CancellationToken cancellationToken) =>
        index.GetAsync(query.DocumentId, TenantScope.For(query.TenantId), cancellationToken);
}
