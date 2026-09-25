using OrderApi.Application;
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi.Results;

namespace OrderApi.Api;

/// <summary>The order endpoints: the HTTP boundary sends a command or query and maps the <c>Result</c>.</summary>
internal static class OrderEndpoints
{
    /// <summary>Maps <c>POST /orders</c> and <c>GET /orders/{id}</c>.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // Result<T> -> HTTP is a single call. The endpoint never inspects IsSuccess and never chooses a status
        // code: Error.Validation becomes 400, Error.NotFound becomes 404, and every failure body is RFC 9457
        // ProblemDetails.
        app.MapPost("/orders", async (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return result.ToProblemDetailsResult(id => Results.Created($"/orders/{id}", new { id }));
        });

        app.MapGet("/orders/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetOrderQuery(id), ct);
            return result.ToProblemDetailsResult();
        });

        return app;
    }
}
