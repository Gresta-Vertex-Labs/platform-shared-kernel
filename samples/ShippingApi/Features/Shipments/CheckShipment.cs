using SharedKernel.Application.Messaging;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>
/// Publishes <see cref="FailingShipmentCheck"/>, whose consumer always fails: exercises retry, then the fault consumer,
/// against a real broker.
/// </summary>
/// <param name="ShipmentId">The shipment.</param>
public sealed record CheckShipment(Guid ShipmentId) : ICommand;

public sealed class CheckShipmentHandler(IMessageBus bus) : ICommandHandler<CheckShipment>
{
    public Task<Result> Handle(CheckShipment command, CancellationToken cancellationToken) =>
        bus.PublishAsync(new FailingShipmentCheck(command.ShipmentId), cancellationToken);
}
