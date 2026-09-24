using BillingApi.Application;
using BillingApi.Domain;
using BillingApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Extensions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Api;

public sealed record RegisterCustomerRequest(string Name, string Email, string? TaxNumber);

public sealed record RenameCustomerRequest(string Name);

public sealed record DraftInvoiceRequest(Guid CustomerId, string Currency, string TaxRate, IReadOnlyList<InvoiceLineInput> Lines);

public sealed record PaymentRequest(decimal Amount, string Reference);

public sealed record ExpireDraftsRequest(DateTimeOffset DraftedBefore);

/// <summary>The body of a 201 Created answer: the id of the new resource, whose URI is in <c>Location</c>.</summary>
public sealed record ResourceCreated(Guid Id);

/// <summary>The body of <c>POST /invoices/expire-drafts</c>.</summary>
public sealed record DraftsExpired(int Expired);

/// <summary>The body of <c>POST /admin/tenants/{tenantId}/erase</c>.</summary>
public sealed record TenantErased(Guid TenantId, bool IsComplete, long BlindIndexValuesCleared);

/// <summary>
/// The HTTP surface. Endpoints translate HTTP to commands and queries and the <see cref="Result"/> they return into a
/// typed result — <c>ToOk()</c>, <c>ToCreated()</c>, <c>ToNoContent()</c>, <c>ToOkWithETag()</c>. They never branch
/// on <c>IsSuccess</c> and never choose a status code for a failure: the <see cref="Error"/> does (400/401/403/404/409,
/// and 412 for a version conflict of a request that named its version in <c>If-Match</c>), and the body is an RFC 9457
/// problem.
/// </summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        MapCustomers(app.MapGroup("/customers"));
        MapInvoices(app.MapGroup("/invoices"));
        MapAdministration(app.MapGroup("/admin"));

        app.MapGet("/reports/revenue", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetRevenue(), ct).ToOk());

        app.MapGet("/audit/{resourceType}/{resourceId}", (string resourceType, string resourceId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetAuditHistory(resourceType, resourceId), ct).ToOk());

        app.MapGet("/audit/{resourceType}", (string resourceType, ISender sender, CancellationToken ct) =>
            sender.Send(new VerifyAuditChain(resourceType), ct).ToOk());
    }

    private static void MapCustomers(RouteGroupBuilder customers)
    {
        customers.MapPost("/", (RegisterCustomerRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new RegisterCustomer(CustomerId.New(), body.Name, body.Email, body.TaxNumber), ct)
                .ToCreated(id => $"/customers/{id.Value}", id => new ResourceCreated(id.Value)));

        // The customer's version travels as its ETag — an opaque token, never PostgreSQL's xmin. A GET whose
        // If-None-Match names the current version gets 304.
        customers.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetCustomer(new CustomerId(id)), ct)
                .ToOkWithETag(found => found.Version.ToString(), found => found.Customer));

        customers.MapGet("/", ([FromQuery] string email, ISender sender, CancellationToken ct) =>
            sender.Send(new GetCustomerByEmail(email), ct).ToOk());

        // Optimistic concurrency: the client sends back the ETag it read. Declaring IfMatch<EntityVersion> requires the
        // header, so before the handler runs a request without it (or with "*", which names no version) gets 428, and a
        // tag that is not an EntityVersion at all, such as a plain number, 412. A version that is not current — another
        // writer saved first, or it belongs to another customer — fails the save with a ConflictException
        // (persistence.concurrency_conflict), answered 412 because the request named its version in If-Match.
        // Success returns the new version as the ETag.
        customers.MapPut("/{id:guid}/name", (Guid id, RenameCustomerRequest body, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
            sender.Send(new RenameCustomer(new CustomerId(id), body.Name, ifMatch.Version), ct)
                .Bind(() => sender.Send(new GetCustomer(new CustomerId(id)), ct))
                .ToOkWithETag(found => found.Version.ToString(), found => found.Customer));

        // A delete names the version it removes, the same way.
        customers.MapDelete("/{id:guid}", (Guid id, IfMatch<EntityVersion> ifMatch, ISender sender, CancellationToken ct) =>
            sender.Send(new DeleteCustomer(new CustomerId(id), ifMatch.Version), ct).ToNoContent());
    }

    private static void MapInvoices(RouteGroupBuilder invoices)
    {
        invoices.MapPost("/", (DraftInvoiceRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new DraftInvoice(InvoiceId.New(), new CustomerId(body.CustomerId), body.Currency, body.TaxRate, body.Lines), ct)
                .ToCreated(id => $"/invoices/{id.Value}", id => new ResourceCreated(id.Value)));

        invoices.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetInvoice(new InvoiceId(id)), ct).ToOk());

        invoices.MapGet("/", (int? page, int? pageSize, string? status, ISender sender, CancellationToken ct) =>
            Validated(PageRequest.Create(page, pageSize))
                .Bind(request => sender.Send(new ListInvoices(request, ParseStatus(status)), ct))
                .ToOk());

        invoices.MapGet("/browse", (string? cursor, int? limit, ISender sender, CancellationToken ct) =>
            Validated(CursorPageRequest.Create(cursor, limit))
                .Bind(request => sender.Send(new BrowseInvoices(request), ct))
                .ToOk());

        invoices.MapPost("/{id:guid}/issue", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new IssueInvoice(new InvoiceId(id)), ct).ToNoContent());

        invoices.MapPost("/{id:guid}/payments", (Guid id, PaymentRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new PayInvoice(new InvoiceId(id), body.Amount, body.Reference), ct).ToNoContent());

        invoices.MapPost("/expire-drafts", (ExpireDraftsRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new ExpireStaleDrafts(body.DraftedBefore), ct).ToOk(count => new DraftsExpired(count)));
    }

    /// <summary>
    /// The back office; everything here requires <c>billing.admin</c>. A query declares that once, with
    /// <c>[RequirePermission]</c>, and the pipeline enforces it on every path the query can take, so its endpoint does
    /// not repeat it. The erasure sends no command, so it carries the requirement itself, as a native authorization
    /// policy over <c>IUserContext</c>: an anonymous caller gets 401 and a caller without the permission 403.
    /// </summary>
    private static void MapAdministration(RouteGroupBuilder admin)
    {
        admin.MapGet("/reports/revenue-by-tenant", (ISender sender, CancellationToken ct) =>
            sender.Send(new GetRevenueByTenant(), ct).ToOk());

        // GDPR/KVKK erasure of a whole tenant: destroys its data key, so every encrypted value becomes unreadable.
        admin.MapPost("/tenants/{tenantId:guid}/erase", async (
            Guid tenantId,
            ICrossTenantScope crossTenant,
            ITenantEncryptionKeyManager keys,
            CancellationToken ct) =>
        {
            using (crossTenant.Enter($"tenant erasure request for {tenantId}"))
            {
                var result = await keys.ShredTenantAsync(tenantId, cancellationToken: ct);
                return TypedResults.Ok(new TenantErased(result.TenantId, result.IsComplete, result.BlindIndexValuesCleared));
            }
        }).RequirePermission(Permissions.Admin);
    }

    /// <summary>Paging input validated at the edge: the request, or every problem with it in one 400.</summary>
    private static Result<T> Validated<T>(ValidationResult<T> input) =>
        input.IsValid ? Result<T>.Success(input.Value) : Result<T>.Failure(Error.Validation(input.Errors));

    /// <summary>An unknown status filters nothing, as it always has.</summary>
    private static InvoiceStatus? ParseStatus(string? status) =>
        Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}
