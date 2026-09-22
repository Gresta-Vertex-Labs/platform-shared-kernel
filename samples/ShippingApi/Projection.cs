using System.Collections.Concurrent;
using SharedKernel.Application.Context;

namespace ShippingApi;

/// <summary>What a consumer recorded about one shipment.</summary>
/// <param name="ShipmentId">The shipment.</param>
/// <param name="Carrier">The carrier, from the dispatched event.</param>
/// <param name="TrackingNumber">The tracking number, from the dispatched event.</param>
/// <param name="TenantId">
/// The tenant the publishing caller acted for, as the consumer resolved it from
/// <c>IRequestContext</c> — not from the message body, which never carries it.
/// </param>
/// <param name="ActorId">The publishing caller's subject id, likewise resolved, not read from the body.</param>
/// <param name="ActorKind">The kind of actor that published.</param>
/// <param name="Deliveries">
/// How many times a consumer ran for this shipment. Stays at 1 across a redelivery when
/// idempotency is enabled, which is the point of recording it.
/// </param>
/// <param name="HoldReason">Set when a hold command was consumed.</param>
public sealed record ShipmentView(
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber,
    Guid? TenantId,
    string? ActorId,
    ActorKind ActorKind,
    int Deliveries,
    string? HoldReason);

/// <summary>
/// The read model consumers write and the API reads.
/// </summary>
/// <remarks>
/// In memory on purpose. A real service would write through <c>06.Persistence</c> inside the
/// consumer's transaction; what this sample is demonstrating is the messaging path, and a database
/// here would add a second moving part without adding a second thing proved.
/// </remarks>
public sealed class ShipmentProjection
{
    private readonly ConcurrentDictionary<Guid, ShipmentView> _shipments = new();

    /// <summary>Records a dispatch, or counts a redelivery of one already recorded.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <param name="carrier">The carrier.</param>
    /// <param name="trackingNumber">The tracking number.</param>
    /// <param name="caller">The identity the consumer resolved for this delivery.</param>
    public void RecordDispatch(Guid shipmentId, string carrier, string trackingNumber, IRequestContext caller) =>
        _shipments.AddOrUpdate(
            shipmentId,
            _ => new ShipmentView(
                shipmentId,
                carrier,
                trackingNumber,
                caller.TenantId,
                caller.UserId,
                caller.ActorKind,
                Deliveries: 1,
                HoldReason: null),
            (_, existing) => existing with { Deliveries = existing.Deliveries + 1 });

    /// <summary>Records that a hold command was consumed for a shipment.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <param name="reason">Why it was held.</param>
    /// <returns><see langword="true"/> when the shipment was known; otherwise <see langword="false"/>.</returns>
    public bool RecordHold(Guid shipmentId, string reason)
    {
        if (!_shipments.TryGetValue(shipmentId, out ShipmentView? existing))
        {
            return false;
        }

        _shipments[shipmentId] = existing with { HoldReason = reason };
        return true;
    }

    /// <summary>Returns a shipment, or <see langword="null"/> when no consumer has recorded it yet.</summary>
    /// <param name="shipmentId">The shipment.</param>
    /// <returns>The recorded view, or <see langword="null"/>.</returns>
    public ShipmentView? Find(Guid shipmentId) =>
        _shipments.TryGetValue(shipmentId, out ShipmentView? view) ? view : null;
}
