using System.Collections.Concurrent;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace InventoryApi;

/// <summary>A SKU's stock and unit price.</summary>
public sealed record StockLevel(string Sku, int Available, decimal UnitPrice, string Currency);

/// <summary>A reservation, with the caller's correlation id and the idempotency key it arrived with.</summary>
public sealed record Reservation(Guid Id, string Sku, int Quantity, string? CorrelationId, string? IdempotencyKey);

/// <summary>The inventory, in memory: two SKUs, and the reservations made against them.</summary>
public sealed class InventoryStore
{
    public const string SkuNotFound = "inventory.sku_not_found";
    public const string InsufficientStock = "inventory.insufficient_stock";
    public const string ReservationNotFound = "inventory.reservation_not_found";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (int Available, Money UnitPrice)> _stock = new(StringComparer.Ordinal)
    {
        ["sku-1"] = (10, Money.Create(19.99m, Currency.Eur).Value),
        ["sku-2"] = (0, Money.Create(5.00m, Currency.Eur).Value),
    };

    private readonly ConcurrentDictionary<Guid, Reservation> _reservations = new();
    private readonly Dictionary<string, Reservation> _byIdempotencyKey = new(StringComparer.Ordinal);

    public Result<(int Available, Money UnitPrice)> Find(string sku)
    {
        lock (_gate)
        {
            return _stock.TryGetValue(sku, out var entry)
                ? Result<(int, Money)>.Success(entry)
                : Result<(int, Money)>.Failure(Error.NotFound(SkuNotFound, $"There is no SKU '{sku}'."));
        }
    }

    /// <summary>
    /// Reserves stock. A repeated idempotency key returns the reservation it made the first time, so a retried request
    /// never reserves twice.
    /// </summary>
    public Result<Reservation> Reserve(string sku, int quantity, string? correlationId, string? idempotencyKey)
    {
        lock (_gate)
        {
            if (idempotencyKey is not null && _byIdempotencyKey.TryGetValue(idempotencyKey, out Reservation? earlier))
            {
                return Result<Reservation>.Success(earlier);
            }

            if (!_stock.TryGetValue(sku, out var entry))
            {
                return Result<Reservation>.Failure(Error.NotFound(SkuNotFound, $"There is no SKU '{sku}'."));
            }

            if (entry.Available < quantity)
            {
                return Result<Reservation>.Failure(Error.Conflict(
                    InsufficientStock,
                    $"Only {entry.Available} of '{sku}' are available; {quantity} were asked for."));
            }

            _stock[sku] = (entry.Available - quantity, entry.UnitPrice);
            var reservation = new Reservation(Guid.CreateVersion7(), sku, quantity, correlationId, idempotencyKey);
            _reservations[reservation.Id] = reservation;
            if (idempotencyKey is not null)
            {
                _byIdempotencyKey[idempotencyKey] = reservation;
            }

            return Result<Reservation>.Success(reservation);
        }
    }

    public Result<Reservation> GetReservation(Guid id) =>
        _reservations.TryGetValue(id, out Reservation? reservation)
            ? Result<Reservation>.Success(reservation)
            : Result<Reservation>.Failure(Error.NotFound(ReservationNotFound, $"There is no reservation '{id}'."));

    public int ReservationCount => _reservations.Count;
}
