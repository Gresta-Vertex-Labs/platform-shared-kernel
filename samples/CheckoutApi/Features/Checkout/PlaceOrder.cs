using CheckoutApi.Clients;
using InventoryApi.Grpc;
using SharedKernel.Application.Messaging;
using SharedKernel.Communication;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace CheckoutApi.Features.Checkout;

/// <summary>What a checkout returns.</summary>
public sealed record Receipt(Guid ReservationId, string Sku, int Quantity, decimal Total, string Currency);

/// <summary>A SKU's price, as InventoryApi quotes it over gRPC.</summary>
public sealed record Quote(string Sku, int Available, decimal UnitPrice, string Currency);

/// <summary>Buys <paramref name="Quantity"/> of <paramref name="Sku"/>: the price over gRPC, the reservation over REST.</summary>
public sealed record PlaceOrder(string Sku, int Quantity) : ICommand<Receipt>;

/// <summary>The price of a SKU, over gRPC.</summary>
public sealed record GetQuote(string Sku) : IQuery<Quote>;

/// <remarks>
/// Neither call throws for a failure: <c>ToResultAsync()</c> and the typed client return InventoryApi's error, and
/// this handler returns it as its own — a 404 there is a 404 here, with the same code.
/// </remarks>
internal sealed class PlaceOrderHandler(Inventory.InventoryClient stock, IInventoryClient inventory) : ICommandHandler<PlaceOrder, Receipt>
{
    public async Task<Result<Receipt>> Handle(PlaceOrder command, CancellationToken cancellationToken)
    {
        Result<Money> unitPrice = await Pricing.UnitPriceAsync(stock, command.Sku, cancellationToken);
        if (unitPrice.IsFailure)
        {
            return Result<Receipt>.Failure(unitPrice.Error);
        }

        Result<InventoryReservation> reservation = await inventory.ReserveAsync(command.Sku, command.Quantity, cancellationToken);
        if (reservation.IsFailure)
        {
            return Result<Receipt>.Failure(reservation.Error);
        }

        Money total = unitPrice.Value * command.Quantity;
        return Result<Receipt>.Success(new Receipt(reservation.Value.Id, command.Sku, command.Quantity, total.Amount, total.Currency.Code));
    }
}

internal sealed class GetQuoteHandler(Inventory.InventoryClient stock) : IQueryHandler<GetQuote, Quote>
{
    public async Task<Result<Quote>> Handle(GetQuote query, CancellationToken cancellationToken)
    {
        Result<StockReply> reply = await stock.GetStockAsync(new GetStockRequest { Sku = query.Sku }, cancellationToken: cancellationToken)
            .ToResultAsync(cancellationToken);

        return reply.IsFailure
            ? Result<Quote>.Failure(reply.Error)
            : Result<Quote>.Success(new Quote(reply.Value.Sku, reply.Value.Available, reply.Value.UnitPrice.ToDecimal(), reply.Value.UnitPrice.CurrencyCode));
    }
}

internal static class Pricing
{
    public static async Task<Result<Money>> UnitPriceAsync(Inventory.InventoryClient stock, string sku, CancellationToken cancellationToken)
    {
        Result<StockReply> reply = await stock.GetStockAsync(new GetStockRequest { Sku = sku }, cancellationToken: cancellationToken)
            .ToResultAsync(cancellationToken);
        if (reply.IsFailure)
        {
            return Result<Money>.Failure(reply.Error);
        }

        ValidationResult<Money> price = reply.Value.UnitPrice.ToMoney();
        return price.IsValid
            ? Result<Money>.Success(price.Value)
            : Result<Money>.Failure(Error.Unexpected("checkout.invalid_price", $"InventoryApi quoted an invalid price for '{sku}'."));
    }
}
