using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace ShippingApi.Features.Shipments;

/// <summary>Reads the projection a consumer wrote.</summary>
/// <param name="ShipmentId">The shipment.</param>
public sealed record GetShipment(Guid ShipmentId) : IQuery<ShipmentView>;

/// <summary>
/// Not found until the consumer has run — the honest answer for an asynchronous write, and what makes the round trip
/// observable from outside.
/// </summary>
public sealed class GetShipmentHandler(ShipmentProjection projection) : IQueryHandler<GetShipment, ShipmentView>
{
    public Task<Result<ShipmentView>> Handle(GetShipment query, CancellationToken cancellationToken) =>
        Task.FromResult(projection.Find(query.ShipmentId) is { } view
            ? Result<ShipmentView>.Success(view)
            : Result<ShipmentView>.Failure(ShipmentErrors.NotFound(query.ShipmentId)));
}
