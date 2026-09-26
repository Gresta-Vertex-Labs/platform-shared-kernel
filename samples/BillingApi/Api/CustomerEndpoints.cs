using BillingApi.Domain;
using BillingApi.Features.Customers;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Presentation.WebApi;

namespace BillingApi.Api;

public sealed record RegisterCustomerRequest(string Name, string Email, string? TaxNumber);

public sealed record RenameCustomerRequest(string Name);

/// <summary>
/// <c>/customers</c>. Each endpoint turns HTTP into a command or query, sends it, and maps the <c>Result</c> to a typed
/// result — <c>ToCreated()</c>, <c>ToOk()</c>, <c>ToOkWithETag()</c>, <c>ToNoContent()</c> — never branching on
/// <c>IsSuccess</c> or choosing a status code for a failure. The permissions are on the commands and queries
/// (<c>[RequirePermission]</c>), not here.
/// </summary>
public sealed class CustomerEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/customers");

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
}
