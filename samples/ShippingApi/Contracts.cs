using SharedKernel.Contracts.Events;

namespace ShippingApi;

/// <summary>
/// Published when a shipment leaves the warehouse. The fact other services care about, not the
/// aggregate that produced it.
/// </summary>
/// <remarks>
/// Primitive members only, as an integration event must be: a domain type here would force every
/// subscriber to depend on this service's model. The name is the wire contract — renaming the
/// record is free, renaming the attribute is a breaking change.
/// </remarks>
/// <param name="EventId">Identifies this occurrence; a retried publish of the same fact reuses it.</param>
/// <param name="OccurredOn">When the shipment was dispatched — not when it was published.</param>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Carrier">The carrier that took it.</param>
/// <param name="TrackingNumber">The carrier's tracking number.</param>
[IntegrationEvent("shipping.shipment-dispatched", Version = 1)]
public sealed record ShipmentDispatched(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber) : IIntegrationEvent;

/// <summary>
/// A command sent to one endpoint, not broadcast: exactly one consumer should put a shipment on
/// hold, whereas any number may care that one was dispatched.
/// </summary>
/// <param name="ShipmentId">The shipment to hold.</param>
/// <param name="Reason">Why, recorded on the projection and in the audit trail.</param>
public sealed record HoldShipment(Guid ShipmentId, string Reason);

/// <summary>
/// A command whose consumer always throws, so the retry and dead-letter paths can be exercised
/// against a real broker rather than described in a comment.
/// </summary>
/// <param name="ShipmentId">The shipment the failing work refers to.</param>
public sealed record FailingShipmentCheck(Guid ShipmentId);

/// <summary>
/// A reminder delivered by the broker at a chosen time, used to show that deferral is
/// transport-native and survives this process restarting.
/// </summary>
/// <param name="ShipmentId">The shipment to chase.</param>
public sealed record ChaseShipment(Guid ShipmentId);
