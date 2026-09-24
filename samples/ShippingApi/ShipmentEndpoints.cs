using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace ShippingApi;

/// <summary>The queue names this service routes commands to.</summary>
/// <remarks>
/// Named constants because a queue name is a wire contract: the sender and the receiving endpoint
/// must agree byte for byte, and a typo produces a queue nobody reads rather than an error.
/// </remarks>
public static class Queues
{
    /// <summary>The endpoint <see cref="HoldShipment"/> is sent to.</summary>
    public const string Hold = "shipping-api-hold-shipment";
}

/// <summary>The errors the read endpoints return.</summary>
public static class ShipmentErrors
{
    /// <summary>No consumer has recorded the shipment: it was never dispatched, or its event has not arrived yet.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <returns>A not-found error, answered 404.</returns>
    public static Error NotFound(Guid shipmentId) =>
        Error.NotFound("shipment.not_found", $"Shipment {shipmentId} has not been recorded.");

    /// <summary>The fault consumer has observed no fault for the shipment.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <returns>A not-found error, answered 404.</returns>
    public static Error NoFault(Guid shipmentId) =>
        Error.NotFound("shipment.fault_not_found", $"No fault has been observed for shipment {shipmentId}.");
}

/// <summary>The HTTP surface of the sample.</summary>
/// <remarks>
/// Every messaging verb returns a <see cref="Result"/> — an unreachable broker is <c>messaging.unavailable</c>, not an
/// exception — and each endpoint maps it with one call: <c>ToAccepted(location)</c>, or <c>ToHttpResult(…)</c> when the
/// 202 carries a body. Success is 202 Accepted with a <c>Location</c> to watch, failure an RFC 9457 problem (503 for the
/// outage). No endpoint branches on <c>IsSuccess</c>.
/// </remarks>
public static class ShipmentEndpoints
{
    /// <summary>Maps every shipment endpoint.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        // Publish: broadcast a fact. Every subscriber gets it; nobody is named. 202, not 201: the shipment exists once
        // a consumer has recorded it, at the Location returned.
        app.MapPost("/shipments", (
            DispatchRequest request,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            var shipmentId = Guid.CreateVersion7();

            var dispatched = new ShipmentDispatched(
                EventId: Guid.CreateVersion7(),
                OccurredOn: DateTimeOffset.UtcNow,
                ShipmentId: shipmentId,
                Carrier: request.Carrier,
                TrackingNumber: request.TrackingNumber);

            // The tenant and actor are NOT passed here. They are read from IRequestContext by the
            // propagator and put on the message, which is what lets the consumer rebuild them.
            return publisher.PublishAsync(dispatched, ct)
                .ToHttpResult(() => TypedResults.Accepted($"/shipments/{shipmentId}", new { shipmentId }));
        });

        // Read the projection a consumer wrote. 404 until the consumer has run — the honest answer
        // for an asynchronous write, and what makes the round trip observable from outside.
        app.MapGet("/shipments/{id:guid}", (Guid id, ShipmentProjection projection) =>
        {
            Result<ShipmentView> shipment = projection.Find(id) is { } view ? view : ShipmentErrors.NotFound(id);
            return shipment.ToOk();
        });

        // Send: address one endpoint. Exactly one consumer holds a shipment, however many replicas run.
        app.MapPost("/shipments/{id:guid}/hold", (
            Guid id,
            HoldRequest request,
            IMessageBus bus,
            CancellationToken ct) =>
            bus.SendAsync(new HoldShipment(id, request.Reason), ct)
                .ToAccepted($"/shipments/{id}"));

        // Schedule: the broker holds the message until its time, so it survives this process exiting.
        app.MapPost("/shipments/{id:guid}/chase", async (
            Guid id,
            ChaseRequest request,
            IMessageScheduler scheduler,
            CancellationToken ct) =>
        {
            Guid token = await scheduler.ScheduleAsync(
                new ChaseShipment(id),
                DateTimeOffset.UtcNow.AddMilliseconds(request.DelayMilliseconds),
                ct);

            return TypedResults.Accepted($"/shipments/{id}", new { scheduleToken = token });
        });

        // Fail on purpose: exercises retry, then the fault consumer, against a real broker.
        app.MapPost("/shipments/{id:guid}/check", (
            Guid id,
            IMessageBus bus,
            CancellationToken ct) =>
            bus.PublishAsync(new FailingShipmentCheck(id), ct)
                .ToAccepted($"/shipments/{id}/fault"));

        // What the fault consumer observed once retries were exhausted.
        app.MapGet("/shipments/{id:guid}/fault", (Guid id, FaultLog faults) =>
        {
            Result<string> fault = faults.Find(id) is { } message ? message : ShipmentErrors.NoFault(id);
            return fault.ToOk(message => new { message });
        });

        return app;
    }
}

/// <summary>Body of <c>POST /shipments</c>.</summary>
/// <param name="Carrier">The carrier taking the shipment.</param>
/// <param name="TrackingNumber">The carrier's tracking number.</param>
public sealed record DispatchRequest(string Carrier, string TrackingNumber);

/// <summary>Body of <c>POST /shipments/{id}/hold</c>.</summary>
/// <param name="Reason">Why the shipment is being held.</param>
public sealed record HoldRequest(string Reason);

/// <summary>Body of <c>POST /shipments/{id}/chase</c>.</summary>
/// <param name="DelayMilliseconds">How long the broker should hold the reminder.</param>
public sealed record ChaseRequest(int DelayMilliseconds);
