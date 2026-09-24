using SharedKernel.Primitives.Errors;

namespace ShippingApi.Features.Shipments;

/// <summary>The errors the read queries return.</summary>
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
