using SharedKernel.Application;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>Dispatches a shipment by publishing <see cref="ShipmentDispatched"/>; returns the new shipment's id.</summary>
/// <param name="Carrier">The carrier taking the shipment.</param>
/// <param name="TrackingNumber">The carrier's tracking number.</param>
public sealed record DispatchShipment(string Carrier, string TrackingNumber) : ICommand<Guid>;

/// <summary>
/// Publish: broadcast a fact. Every subscriber gets it; nobody is named. The shipment exists once a consumer has
/// recorded it. An unreachable broker is a failed <see cref="Result"/> (<c>messaging.unavailable</c>), not an exception.
/// </summary>
public sealed class DispatchShipmentHandler(IEventPublisher publisher, IClock clock) : ICommandHandler<DispatchShipment, Guid>
{
    public async Task<Result<Guid>> Handle(DispatchShipment command, CancellationToken cancellationToken)
    {
        var shipmentId = Guid.CreateVersion7();

        var dispatched = new ShipmentDispatched(
            EventId: Guid.CreateVersion7(),
            OccurredOn: clock.UtcNow,
            ShipmentId: shipmentId,
            Carrier: command.Carrier,
            TrackingNumber: command.TrackingNumber);

        // The tenant and actor are NOT passed here. They are read from IRequestContext by the
        // propagator and put on the message, which is what lets the consumer rebuild them.
        var published = await publisher.PublishAsync(dispatched, cancellationToken);
        return published.IsSuccess ? Result<Guid>.Success(shipmentId) : Result<Guid>.Failure(published.Error);
    }
}
