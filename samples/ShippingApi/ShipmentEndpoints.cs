using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;
using ShippingApi.Features.Shipments;

namespace ShippingApi;

/// <summary>The HTTP surface of the sample.</summary>
/// <remarks>
/// Each endpoint sends a command or query (<c>Features/Shipments</c>) and maps the <c>Result</c> with one call:
/// <c>ToAccepted(location)</c>, <c>ToOk()</c>, or <c>ToHttpResult(…)</c> when the 202 carries a body. Every messaging verb
/// returns a <c>Result</c> — an unreachable broker is <c>messaging.unavailable</c>, not an exception — so success is 202
/// Accepted with a <c>Location</c> to watch and failure an RFC 9457 problem (503 for the outage). No endpoint branches on
/// <c>IsSuccess</c>.
/// </remarks>
public sealed class ShipmentEndpoints : IEndpointModule
{
    /// <summary>Maps every shipment endpoint; called by the generated <c>app.MapEndpoints()</c>.</summary>
    /// <param name="app">The route builder.</param>
    public static void Map(IEndpointRouteBuilder app)
    {
        var shipments = app.MapGroup("/shipments");

        // 202, not 201: the shipment exists once a consumer has recorded it, at the Location returned.
        shipments.MapPost("/", (DispatchRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new DispatchShipment(request.Carrier, request.TrackingNumber), ct)
                .ToHttpResult(id => TypedResults.Accepted($"/shipments/{id}", new ShipmentAccepted(id))));

        // 404 until the consumer has run.
        shipments.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetShipment(id), ct).ToOk());

        shipments.MapPost("/{id:guid}/hold", (Guid id, HoldRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new PutShipmentOnHold(id, request.Reason), ct).ToAccepted($"/shipments/{id}"));

        shipments.MapPost("/{id:guid}/chase", (Guid id, ChaseRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new ScheduleChase(id, request.DelayMilliseconds), ct)
                .ToHttpResult(token => TypedResults.Accepted($"/shipments/{id}", new ChaseScheduled(token))));

        // Fail on purpose: the fault shows up at the Location once retries are exhausted.
        shipments.MapPost("/{id:guid}/check", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new CheckShipment(id), ct).ToAccepted($"/shipments/{id}/fault"));

        shipments.MapGet("/{id:guid}/fault", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetShipmentFault(id), ct).ToOk());
    }
}

/// <summary>Body of <c>POST /shipments</c>.</summary>
/// <param name="Carrier">The carrier taking the shipment.</param>
/// <param name="TrackingNumber">The carrier's tracking number.</param>
public sealed record DispatchRequest(string Carrier, string TrackingNumber);

/// <summary>Body of the 202 answer to <c>POST /shipments</c>.</summary>
/// <param name="ShipmentId">The shipment, readable at the <c>Location</c> once a consumer has recorded it.</param>
public sealed record ShipmentAccepted(Guid ShipmentId);

/// <summary>Body of <c>POST /shipments/{id}/hold</c>.</summary>
/// <param name="Reason">Why the shipment is being held.</param>
public sealed record HoldRequest(string Reason);

/// <summary>Body of <c>POST /shipments/{id}/chase</c>.</summary>
/// <param name="DelayMilliseconds">How long the broker should hold the reminder.</param>
public sealed record ChaseRequest(int DelayMilliseconds);

/// <summary>Body of the 202 answer to <c>POST /shipments/{id}/chase</c>.</summary>
/// <param name="ScheduleToken">The broker's token for the scheduled message.</param>
public sealed record ChaseScheduled(Guid ScheduleToken);
