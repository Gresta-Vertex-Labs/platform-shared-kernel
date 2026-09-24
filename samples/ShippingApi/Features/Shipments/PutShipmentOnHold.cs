using SharedKernel.Application;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>Puts a shipment on hold by sending <see cref="HoldShipment"/> to the one endpoint that holds shipments.</summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Reason">Why it is held.</param>
public sealed record PutShipmentOnHold(Guid ShipmentId, string Reason) : ICommand;

/// <summary>Send: address one endpoint. Exactly one consumer holds a shipment, however many replicas run.</summary>
public sealed class PutShipmentOnHoldHandler(IMessageBus bus) : ICommandHandler<PutShipmentOnHold>
{
    public Task<Result> Handle(PutShipmentOnHold command, CancellationToken cancellationToken) =>
        bus.SendAsync(new HoldShipment(command.ShipmentId, command.Reason), cancellationToken);
}
