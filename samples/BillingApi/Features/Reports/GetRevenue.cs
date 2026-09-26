using Dapper;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Reports;

public sealed record RevenueLine(string Currency, long Invoices, decimal Gross, decimal Received);

/// <summary>The caller's own revenue: hand-written SQL, no tenant predicate — row-level security scopes it.</summary>
[RequirePermission(Permissions.Read)]
public sealed record GetRevenue : IQuery<IReadOnlyList<RevenueLine>>;

public sealed class GetRevenueHandler(IDbSessionFactory sessions) : IQueryHandler<GetRevenue, IReadOnlyList<RevenueLine>>
{
    public async Task<Result<IReadOnlyList<RevenueLine>>> Handle(GetRevenue query, CancellationToken cancellationToken)
    {
        await using var session = await sessions.OpenReadOnlyAsync(cancellationToken);
        var rows = await session.Connection.QueryAsync<RevenueLine>(session.Command(
            """
            SELECT i.gross_currency AS currency,
                   count(*) AS invoices,
                   sum(i.gross_amount) AS gross,
                   coalesce(sum(p.amount), 0) AS received
            FROM invoices i
            LEFT JOIN payments p ON p.invoice_id = i.id
            WHERE i.status IN ('Issued', 'Paid') AND NOT i.is_deleted
            GROUP BY i.gross_currency
            ORDER BY i.gross_currency
            """,
            cancellationToken: cancellationToken));
        return Result<IReadOnlyList<RevenueLine>>.Success([.. rows]);
    }
}
