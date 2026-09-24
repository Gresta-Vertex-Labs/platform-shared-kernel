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
