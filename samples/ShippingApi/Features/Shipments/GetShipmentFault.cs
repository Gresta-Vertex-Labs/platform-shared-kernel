using SharedKernel.Application;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>What the fault consumer observed for a shipment once retries were exhausted.</summary>
/// <param name="Message">The exception message the consumer failed with.</param>
public sealed record ShipmentFault(string Message);

/// <summary>Reads the fault the fault consumer recorded for a shipment.</summary>
/// <param name="ShipmentId">The shipment.</param>
public sealed record GetShipmentFault(Guid ShipmentId) : IQuery<ShipmentFault>;

public sealed class GetShipmentFaultHandler(FaultLog faults) : IQueryHandler<GetShipmentFault, ShipmentFault>
{
    public Task<Result<ShipmentFault>> Handle(GetShipmentFault query, CancellationToken cancellationToken) =>
        Task.FromResult(faults.Find(query.ShipmentId) is { } message
            ? Result<ShipmentFault>.Success(new ShipmentFault(message))
            : Result<ShipmentFault>.Failure(ShipmentErrors.NoFault(query.ShipmentId)));
}
