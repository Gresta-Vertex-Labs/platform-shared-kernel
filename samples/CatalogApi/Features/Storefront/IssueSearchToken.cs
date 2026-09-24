using SharedKernel.Application;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tenancy;

namespace CatalogApi.Features.Storefront;

/// <summary>
/// A signed, expiring token a browser holds. The tenant filter inside it is enforced by the ENGINE, not by this
/// service — so a compromised front end still cannot read another tenant. Meilisearch-exclusive.
/// </summary>
public sealed record IssueSearchToken(string TenantId) : ICommand<TenantSearchToken>;

public sealed class IssueSearchTokenHandler(ITenantSearchTokenIssuer issuer) : ICommandHandler<IssueSearchToken, TenantSearchToken>
{
    public Task<Result<TenantSearchToken>> Handle(IssueSearchToken command, CancellationToken cancellationToken) =>
        issuer.IssueAsync(
            TenantScope.Of(command.TenantId),
            ProductFields.TenantId,
            [Catalog.ProductsIndex],
            TimeSpan.FromMinutes(5),
            cancellationToken);
}
