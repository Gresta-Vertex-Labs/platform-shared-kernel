using SharedKernel.Execution.Tenancy;
using BillingApi.Application;
using BillingApi.Domain;
using BillingApi.Infrastructure;
using SharedKernel.Application.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SharedKernel.Execution.Context;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Api;

public sealed record RegisterCustomerRequest(string Name, string Email, string? TaxNumber);

public sealed record RenameCustomerRequest(string Name);

public sealed record DraftInvoiceRequest(Guid CustomerId, string Currency, string TaxRate, IReadOnlyList<InvoiceLineInput> Lines);

public sealed record PaymentRequest(decimal Amount, string Reference);

public sealed record ExpireDraftsRequest(DateTimeOffset DraftedBefore);

/// <summary>
/// The HTTP surface. Endpoints translate HTTP to commands and queries and back; they never choose a status code for
/// a failure — <c>ToProblemDetailsResult()</c> maps the <see cref="Error"/> type (400/401/403/404/409).
/// </summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        MapCustomers(app.MapGroup("/customers"));
        MapInvoices(app.MapGroup("/invoices"));

        app.MapGet("/reports/revenue", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetRevenue(), ct)).ToProblemDetailsResult());

        app.MapGet("/admin/reports/revenue-by-tenant", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetRevenueByTenant(), ct)).ToProblemDetailsResult());

        app.MapGet("/audit/{resourceType}/{resourceId}", async (string resourceType, string resourceId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetAuditHistory(resourceType, resourceId), ct)).ToProblemDetailsResult());

        app.MapGet("/audit/{resourceType}", async (string resourceType, ISender sender, CancellationToken ct) =>
            (await sender.Send(new VerifyAuditChain(resourceType), ct)).ToProblemDetailsResult());

        // GDPR/KVKK erasure of a whole tenant: destroys its data key, so every encrypted value becomes unreadable.
        app.MapPost("/admin/tenants/{tenantId:guid}/erase", async (
            Guid tenantId,
            IRequestContext caller,
            ICrossTenantScope crossTenant,
            ITenantEncryptionKeyManager keys,
            CancellationToken ct) =>
        {
            if (!caller.IsAuthenticated)
                return Result.Failure(Error.Unauthorized("auth.required", "Sign in first.")).ToProblemDetailsResult();
            if (!await caller.HasPermissionAsync(Permissions.Admin, ct))
                return Result.Failure(Error.Forbidden("auth.forbidden", "billing.admin is required.")).ToProblemDetailsResult();

            using (crossTenant.Enter($"tenant erasure request for {tenantId}"))
            {
                var result = await keys.ShredTenantAsync(new TenantId(tenantId), cancellationToken: ct);
                return Results.Ok(new { result.TenantId, result.IsComplete, result.BlindIndexValuesCleared });
            }
        });
    }

    private static void MapCustomers(RouteGroupBuilder customers)
    {
        customers.MapPost("/", async (RegisterCustomerRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RegisterCustomer(CustomerId.New(), body.Name, body.Email, body.TaxNumber), ct);
            return result.ToProblemDetailsResult(id => Results.Created($"/customers/{id.Value}", new { id = id.Value }));
        });

        customers.MapGet("/{id:guid}", async (Guid id, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCustomer(new CustomerId(id)), ct);
            return result.ToProblemDetailsResult(found =>
            {
                http.Response.Headers.ETag = ETag(found.Version);
                return Results.Ok(found.Customer);
            });
        });

        customers.MapGet("/", async ([FromQuery] string email, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetCustomerByEmail(email), ct)).ToProblemDetailsResult());

        // Optimistic concurrency: the client sends back the ETag it read. A missing If-Match is 428, a stale one 412.
        customers.MapPut("/{id:guid}/name", async (Guid id, RenameCustomerRequest body, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            if (!EntityVersion.TryParse(http.Request.Headers.IfMatch.ToString(), out var expected))
                return Results.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match required");

            try
            {
                var renamed = await sender.Send(new RenameCustomer(new CustomerId(id), body.Name, expected), ct);
                if (renamed.IsFailure)
                    return renamed.ToProblemDetailsResult();
            }
            catch (ConflictException conflict) when (ConcurrencyVersion.TryGetCurrentVersion(conflict, out var current))
            {
                http.Response.Headers.ETag = ETag(current);
                return Results.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: conflict.Error.Code, detail: conflict.Error.Message);
            }

            var reread = await sender.Send(new GetCustomer(new CustomerId(id)), ct);
            return reread.ToProblemDetailsResult(found =>
            {
                http.Response.Headers.ETag = ETag(found.Version);
                return Results.Ok(found.Customer);
            });
        });

        customers.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeleteCustomer(new CustomerId(id)), ct)).ToProblemDetailsResult());
    }

    private static void MapInvoices(RouteGroupBuilder invoices)
    {
        invoices.MapPost("/", async (DraftInvoiceRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new DraftInvoice(InvoiceId.New(), new CustomerId(body.CustomerId), body.Currency, body.TaxRate, body.Lines);
            var result = await sender.Send(command, ct);
            return result.ToProblemDetailsResult(id => Results.Created($"/invoices/{id.Value}", new { id = id.Value }));
        });

        invoices.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetInvoice(new InvoiceId(id)), ct)).ToProblemDetailsResult());

        invoices.MapGet("/", async (int? page, int? pageSize, string? status, ISender sender, CancellationToken ct) =>
        {
            var request = PageRequest.Create(page, pageSize);
            if (!request.IsValid)
                return Result.Failure(Error.Validation(request.Errors)).ToProblemDetailsResult();

            InvoiceStatus? filter = Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
            return (await sender.Send(new ListInvoices(request.Value, filter), ct)).ToProblemDetailsResult();
        });

        invoices.MapGet("/browse", async (string? cursor, int? limit, ISender sender, CancellationToken ct) =>
        {
            var request = CursorPageRequest.Create(cursor, limit);
            if (!request.IsValid)
                return Result.Failure(Error.Validation(request.Errors)).ToProblemDetailsResult();

            return (await sender.Send(new BrowseInvoices(request.Value), ct)).ToProblemDetailsResult();
        });

        invoices.MapPost("/{id:guid}/issue", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new IssueInvoice(new InvoiceId(id)), ct)).ToProblemDetailsResult());

        invoices.MapPost("/{id:guid}/payments", async (Guid id, PaymentRequest body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PayInvoice(new InvoiceId(id), body.Amount, body.Reference), ct)).ToProblemDetailsResult());

        invoices.MapPost("/expire-drafts", async (ExpireDraftsRequest body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ExpireStaleDrafts(body.DraftedBefore), ct)).ToProblemDetailsResult(count => Results.Ok(new { expired = count })));
    }

    private static string ETag(EntityVersion version) => new EntityTagHeaderValue($"\"{version}\"").ToString();
}
