using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Scheduling;
using SharedKernel.Presentation.WebApi.Results;
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

/// <summary>The HTTP surface of the sample.</summary>
public static class ShipmentEndpoints
{
    /// <summary>Maps every shipment endpoint.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        // Publish: broadcast a fact. Every subscriber gets it; nobody is named.
        app.MapPost("/shipments", async (
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
            Result published = await publisher.PublishAsync(dispatched, ct);

            return published.IsSuccess
                ? Results.Accepted($"/shipments/{shipmentId}", new { shipmentId })
                : published.ToProblemDetailsResult();
        });

        // Read the projection a consumer wrote. 404 until the consumer has run — the honest answer
        // for an asynchronous write, and what makes the round trip observable from outside.
        app.MapGet("/shipments/{id:guid}", (Guid id, ShipmentProjection projection) =>
        {
            ShipmentView? view = projection.Find(id);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

        // Send: address one endpoint. Exactly one consumer holds a shipment, however many replicas run.
        app.MapPost("/shipments/{id:guid}/hold", async (
            Guid id,
            HoldRequest request,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            Result sent = await bus.SendAsync(new HoldShipment(id, request.Reason), ct);
            return sent.IsSuccess ? Results.Accepted() : sent.ToProblemDetailsResult();
        });

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

            return Results.Accepted(value: new { scheduleToken = token });
        });

        // Fail on purpose: exercises retry, then the fault consumer, against a real broker.
        app.MapPost("/shipments/{id:guid}/check", async (
            Guid id,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            Result published = await bus.PublishAsync(new FailingShipmentCheck(id), ct);
            return published.IsSuccess ? Results.Accepted() : published.ToProblemDetailsResult();
        });

        // What the fault consumer observed once retries were exhausted.
        app.MapGet("/shipments/{id:guid}/fault", (Guid id, FaultLog faults) =>
        {
            string? fault = faults.Find(id);
            return fault is null ? Results.NotFound() : Results.Ok(new { message = fault });
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
