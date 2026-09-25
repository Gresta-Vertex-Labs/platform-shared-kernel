using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.OpenApi.Tests.TestSupport;

/// <summary>A versioned minimal API with every kind of endpoint the documents describe differently.</summary>
internal static class OrdersApi
{
    public const string ReadPermission = "orders.read";

    public const string WritePermission = "orders.write";

    public const string AdminPermission = "orders.admin";

    public static void Map(WebApplication app)
    {
        var orders = app.NewVersionedApi("Orders")
            .MapGroup("/v{version:apiVersion}/orders")
            .HasApiVersion(1.0)
            .HasApiVersion(2.0);

        orders.MapGet("/", () => Result<Order[]>.Success([new Order(1)]).ToOk());
        orders.MapGet("/{id:int}", (int id) => Result<Order>.Success(new Order(id)).ToOk())
            .RequireEndpointPermission(ReadPermission);
        orders.MapPost("/", (Order order) => Result<Order>.Success(order).ToCreated(created => $"/v1/orders/{created.Id}"))
            .RequireEndpointPermission(WritePermission)
            .RequireIdempotencyKey();
        orders.MapPut("/{id:int}", (int id, Order order) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission)
            .RequireIfMatch();
        orders.MapGet("/export", () => "csv").MapToApiVersion(2.0);

        // The same requirements declared by a handler parameter or a lambda attribute instead of a convention.
        orders.MapPost("/{id:int}/payments", (int id, Order order, IdempotencyKey idempotencyKey) => Result.Success().ToAccepted())
            .RequireEndpointPermission(WritePermission);
        orders.MapPost("/{id:int}/cancellation", (int id, IdempotencyKey idempotencyKey) => Result.Success().ToAccepted())
            .RequireEndpointPermission(WritePermission);
        orders.MapPatch("/{id:int}", (int id, Order order, IfMatch<long> ifMatch) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission);
        orders.MapDelete("/{id:int}", [RequireIfMatch] (int id) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission);

        // Both headers at once.
        orders.MapPost("/{id:int}/refunds", (int id, IdempotencyKey idempotencyKey, IfMatch<long> ifMatch) => Result.Success().ToAccepted())
            .RequireEndpointPermission(WritePermission);

        // The headers accepted rather than required: a convention, a nullable parameter or a lambda attribute.
        orders.MapPost("/{id:int}/notes", (int id) => Result.Success().ToAccepted())
            .RequireEndpointPermission(WritePermission)
            .AcceptIdempotencyKey();
        orders.MapPost("/{id:int}/reminders", (int id, IdempotencyKey? idempotencyKey) => Result.Success().ToAccepted())
            .RequireEndpointPermission(WritePermission);
        orders.MapPut("/{id:int}/address", (int id, Order order) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission)
            .AcceptIfMatch();
        orders.MapPatch("/{id:int}/address", (int id, Order order, IfMatch<long>? ifMatch) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission);
        orders.MapDelete("/{id:int}/address", [AcceptIfMatch] (int id) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission);

        // Accepted and required at once: the requirement wins.
        orders.MapPut("/{id:int}/lines", (int id, IfMatch<long>? ifMatch) => Result.Success().ToNoContent())
            .RequireEndpointPermission(WritePermission)
            .RequireIfMatch();

        var admin = orders.MapGroup("/admin").RequireEndpointPermission(AdminPermission);
        admin.MapGet("/stats", () => "stats");
        admin.MapGet("/ping", () => "pong").AllowAnonymous();
    }
}

/// <summary>An order, as the test API returns it.</summary>
public sealed record Order(int Id);

/// <summary>A versioned MVC controller declaring its requirements with the core's attributes.</summary>
[ApiController]
[ApiVersion(1.0)]
[Route("v{version:apiVersion}/mvc/orders")]
public sealed class MvcOrdersController : ControllerBase
{
    /// <summary>Anonymous.</summary>
    [HttpGet]
    public IActionResult List() => Ok(Array.Empty<Order>());

    /// <summary>Protected by an attribute; returns the typed results minimal APIs return.</summary>
    [HttpGet("{id:int}")]
    [RequireEndpointPermission(OrdersApi.ReadPermission)]
    public Results<Ok<Order>, ErrorHttpResult> Get(int id) => Result<Order>.Success(new Order(id)).ToOk();

    /// <summary>Returns the order with its version as an ETag.</summary>
    [HttpGet("{id:int}/receipt")]
    public Results<OkWithETag<Order>, ErrorHttpResult> Receipt(int id) =>
        Result<Order>.Success(new Order(id)).ToOkWithETag(order => order.Id.ToString(CultureInfo.InvariantCulture));

    /// <summary>Requires an idempotency key.</summary>
    [HttpPost]
    [RequireIdempotencyKey]
    public IActionResult Create(Order order) => Ok(order);

    /// <summary>Requires If-Match.</summary>
    [HttpPut("{id:int}")]
    [RequireIfMatch]
    public IActionResult Update(int id, Order order) => NoContent();

    /// <summary>Accepts an idempotency key.</summary>
    [HttpPost("{id:int}/notes")]
    [AcceptIdempotencyKey]
    public IActionResult AddNote(int id) => Accepted();

    /// <summary>Accepts If-Match.</summary>
    [HttpPatch("{id:int}")]
    [AcceptIfMatch]
    public IActionResult Patch(int id, Order order) => NoContent();
}
