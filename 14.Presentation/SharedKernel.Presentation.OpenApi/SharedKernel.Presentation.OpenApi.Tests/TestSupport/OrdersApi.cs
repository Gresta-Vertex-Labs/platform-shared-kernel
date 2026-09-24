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
            .RequirePermission(ReadPermission);
        orders.MapPost("/", (Order order) => Result<Order>.Success(order).ToCreated(created => $"/v1/orders/{created.Id}"))
            .RequirePermission(WritePermission)
            .RequireIdempotencyKey();
        orders.MapPut("/{id:int}", (int id, Order order) => Result.Success().ToNoContent())
            .RequirePermission(WritePermission)
            .RequireIfMatch();
        orders.MapGet("/export", () => "csv").MapToApiVersion(2.0);

        // The same requirements declared by a handler parameter or a lambda attribute instead of a convention.
        orders.MapPost("/{id:int}/payments", (int id, Order order, IdempotencyKey idempotencyKey) => Result.Success().ToAccepted())
            .RequirePermission(WritePermission);
        orders.MapPost("/{id:int}/cancellation", (int id, IdempotencyKey idempotencyKey) => Result.Success().ToAccepted())
            .RequirePermission(WritePermission);
        orders.MapPatch("/{id:int}", (int id, Order order, IfMatch<long> ifMatch) => Result.Success().ToNoContent())
            .RequirePermission(WritePermission);
        orders.MapDelete("/{id:int}", [RequireIfMatch] (int id) => Result.Success().ToNoContent())
            .RequirePermission(WritePermission);

        // Both headers at once.
        orders.MapPost("/{id:int}/refunds", (int id, IdempotencyKey idempotencyKey, IfMatch<long> ifMatch) => Result.Success().ToAccepted())
            .RequirePermission(WritePermission);

        var admin = orders.MapGroup("/admin").RequirePermission(AdminPermission);
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
    [RequirePermission(OrdersApi.ReadPermission)]
    public Results<Ok<Order>, ErrorHttpResult> Get(int id) => Result<Order>.Success(new Order(id)).ToOk();

    /// <summary>Requires an idempotency key.</summary>
    [HttpPost]
    [RequireIdempotencyKey]
    public IActionResult Create(Order order) => Ok(order);

    /// <summary>Requires If-Match.</summary>
    [HttpPut("{id:int}")]
    [RequireIfMatch]
    public IActionResult Update(int id, Order order) => NoContent();
}
