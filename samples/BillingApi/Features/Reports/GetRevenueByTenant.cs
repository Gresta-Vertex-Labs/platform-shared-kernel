using Dapper;
using SharedKernel.Application;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Reports;

public sealed record TenantRevenueLine(Guid TenantId, string Currency, long Invoices, decimal Gross);

/// <summary>Back office: every tenant. Entering the cross-tenant scope moves the Dapper session onto the cross-tenant role.</summary>
[RequirePermission(Permissions.Admin)]
public sealed record GetRevenueByTenant : IQuery<IReadOnlyList<TenantRevenueLine>>;

public sealed class GetRevenueByTenantHandler(IDbSessionFactory sessions, ICrossTenantScope crossTenant)
    : IQueryHandler<GetRevenueByTenant, IReadOnlyList<TenantRevenueLine>>
{
    public async Task<Result<IReadOnlyList<TenantRevenueLine>>> Handle(GetRevenueByTenant query, CancellationToken cancellationToken)
    {
        using (crossTenant.Enter("back-office revenue by tenant report"))   // reason is logged with the caller
        {
            await using var session = await sessions.OpenReadOnlyAsync(cancellationToken);
            var rows = await session.Connection.QueryAsync<TenantRevenueLine>(session.Command(
                """
                SELECT tenant_id, gross_currency AS currency, count(*) AS invoices, sum(gross_amount) AS gross
                FROM invoices
                WHERE status IN ('Issued', 'Paid') AND NOT is_deleted
                GROUP BY tenant_id, gross_currency
                ORDER BY tenant_id, gross_currency
                """,
                cancellationToken: cancellationToken));
            return Result<IReadOnlyList<TenantRevenueLine>>.Success([.. rows]);
        }
    }
}
