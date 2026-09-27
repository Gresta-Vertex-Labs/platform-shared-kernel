using SharedKernel.Communication;
using SharedKernel.Primitives.Results;

namespace CheckoutApi.Clients;

/// <summary>A reservation InventoryApi made.</summary>
public sealed record InventoryReservation(Guid Id, string Sku, int Quantity);

/// <summary>InventoryApi's REST side, as this service uses it.</summary>
public interface IInventoryClient
{
    /// <summary>Reserves stock; the request carries an Idempotency-Key, so its retries never reserve twice.</summary>
    Task<Result<InventoryReservation>> ReserveAsync(string sku, int quantity, CancellationToken cancellationToken);
}

/// <summary>
/// The typed client: one line per call. The HttpClient it is given already has the address, the API key, the caller's
/// headers, the Idempotency-Key and the retry policy, all from <c>SharedKernel:Communication:Clients:inventory</c>.
/// A failure comes back as InventoryApi's own error — <c>inventory.insufficient_stock</c>, field errors — or as
/// <c>communication.unreachable</c>/<c>communication.timeout</c> when it did not answer.
/// </summary>
public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<InventoryReservation>> ReserveAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        http.PostResultAsync<ReserveRequest, InventoryReservation>("reservations", new ReserveRequest(sku, quantity), cancellationToken: cancellationToken);

    private sealed record ReserveRequest(string Sku, int Quantity);
}
