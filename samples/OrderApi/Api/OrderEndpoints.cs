using MediatR;
using OrderApi.Features.Orders;
using SharedKernel.Presentation.WebApi;

namespace OrderApi.Api;

/// <summary>The body of the 201 Created answer to <c>POST /orders</c>.</summary>
/// <param name="Id">The id of the new order.</param>
public sealed record OrderPlaced(Guid Id);

/// <summary>
/// Version 1.0 of the orders API. Each endpoint turns the request into a command or query, sends it through
/// <see cref="ISender"/>, and maps the <see cref="SharedKernel.Primitives.Results.Result{T}"/> it gets back to a typed
/// result; none of them inspects <c>IsSuccess</c> or chooses a status code for a failure.
/// </summary>
public sealed class OrderEndpoints : IEndpointModule
{
    /// <summary>Maps the orders API; called by the generated <c>app.MapEndpoints()</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void Map(IEndpointRouteBuilder app)
    {
        // A versioned API: every endpoint of the group belongs to version 1.0. A request that names no version is
        // served the default (1.0), so plain /orders keeps working; X-Api-Version: 1.0 selects it explicitly, and
        // the response reports the supported versions in api-supported-versions.
        var orders = app.NewVersionedApi("Orders").MapGroup("/orders").HasApiVersion(1.0);

        // 201 with a Location header on success; a validation failure is a 400 whose errors map lists every field.
        orders.MapPost("/", (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
                sender.Send(command, ct).ToCreated(id => $"/orders/{id}", id => new OrderPlaced(id)))
            .WithName("PlaceOrder")
            .WithSummary("Places an order.");

        // 200 with the order; Error.NotFound from the handler becomes a 404 problem.
        orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
                sender.Send(new GetOrderQuery(id), ct).ToOk())
            .WithName("GetOrder")
            .WithSummary("Returns an order.");
    }
}
