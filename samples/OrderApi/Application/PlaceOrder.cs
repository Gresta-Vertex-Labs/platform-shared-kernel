using OrderApi.Domain;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace OrderApi.Application;

public sealed record PlaceOrderCommand(string Customer, decimal Amount, string Currency, List<string> Lines)
    : ICommand<Guid>;

/// <summary>
/// Returns <see cref="Result{T}"/> and never throws for an expected failure. The pipeline
/// behaviors registered in Program.cs (logging, metrics, tracing, validation) wrap this
/// automatically — the handler itself stays free of cross-cutting concerns.
/// </summary>
public sealed class PlaceOrderHandler(IOrderRepository repository, IClock clock)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var money = Money.Create(request.Amount, request.Currency);
        if (money.IsFailure)
            return Result<Guid>.Failure(money.Error);

        var placed = Order.Place(request.Customer, money.Value, request.Lines, clock);
        if (placed.IsFailure)
            return Result<Guid>.Failure(placed.Error);

        await repository.AddAsync(placed.Value, cancellationToken);
        return Result<Guid>.Success(placed.Value.Id.Value);
    }
}
