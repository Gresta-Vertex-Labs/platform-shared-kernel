using BillingApi.Domain;
using Dapper;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Primitives.Results;

namespace BillingApi.Application;

public sealed record RevenueLine(string Currency, long Invoices, decimal Gross, decimal Received);

public sealed record TenantRevenueLine(Guid TenantId, string Currency, long Invoices, decimal Gross);

// ---------------------------------------------------------------------------------------------------------------
// The caller's own revenue: hand-written SQL, no tenant predicate — row-level security scopes it.
// ---------------------------------------------------------------------------------------------------------------

public sealed record GetRevenue : IQuery<IReadOnlyList<RevenueLine>>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

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

// ---------------------------------------------------------------------------------------------------------------
// Back office: every tenant. Entering the cross-tenant scope moves the Dapper session onto the cross-tenant role.
// ---------------------------------------------------------------------------------------------------------------

public sealed record GetRevenueByTenant : IQuery<IReadOnlyList<TenantRevenueLine>>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Admin];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

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

// ---------------------------------------------------------------------------------------------------------------
// Audit trail
// ---------------------------------------------------------------------------------------------------------------

public sealed record AuditLine(Guid Id, string Action, string Outcome, string ActorId, string ActorKind, DateTimeOffset OccurredOn);

public sealed record GetAuditHistory(string ResourceType, string ResourceId) : IQuery<IReadOnlyList<AuditLine>>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

public sealed class GetAuditHistoryHandler(IAuditQueryService audit) : IQueryHandler<GetAuditHistory, IReadOnlyList<AuditLine>>
{
    public async Task<Result<IReadOnlyList<AuditLine>>> Handle(GetAuditHistory query, CancellationToken cancellationToken)
    {
        var page = await audit.QueryAsync(new AuditRecordQuery { ResourceType = query.ResourceType, ResourceId = query.ResourceId }, cancellationToken);
        return Result<IReadOnlyList<AuditLine>>.Success(
            [.. page.Items.Select(r => new AuditLine(r.Id, r.Action, r.Outcome.ToString(), r.ActorId, r.ActorKind.ToString(), r.OccurredOn))]);
    }
}

public sealed record AuditChainStatus(string ResourceType, string Status, long RecordsChecked, long? HeadSequence, string? FailureKind, string? Reason);

public sealed record VerifyAuditChain(string ResourceType) : IQuery<AuditChainStatus>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

public sealed class VerifyAuditChainHandler(IAuditQueryService audit) : IQueryHandler<VerifyAuditChain, AuditChainStatus>
{
    public async Task<Result<AuditChainStatus>> Handle(VerifyAuditChain query, CancellationToken cancellationToken)
    {
        var result = await audit.VerifyChainAsync(query.ResourceType, cancellationToken: cancellationToken);
        return Result<AuditChainStatus>.Success(new AuditChainStatus(
            query.ResourceType,
            result.Status.ToString(),
            result.RecordsChecked,
            result.HeadSequence,
            result.IsIntact ? null : result.FailureKind.ToString(),
            result.Reason));
    }
}

/// <summary>Resource-type names used by the audit endpoints.</summary>
public static class AuditedResources
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { nameof(Customer), nameof(Invoice) };
}
