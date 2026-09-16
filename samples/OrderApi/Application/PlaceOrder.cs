using FluentValidation;
using OrderApi.Domain;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Application;

public sealed record PlaceOrderCommand(string Customer, decimal Amount, string Currency, List<string> Lines)
    : ICommand<Guid>;

/// <summary>
/// Input-shape rules, run by <c>ValidationBehavior</c> before the handler. Every failing rule is
/// collected into one <c>Error.Validation(errors)</c> and returned as a failed
/// <see cref="Result{T}"/> — nothing is thrown — which the endpoint maps to a 400 whose
/// <c>errors</c> map lists each field. Domain invariants stay in the domain (<see cref="Money"/>,
/// <see cref="Order"/>).
/// </summary>
public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.Customer).NotEmpty();
        RuleFor(c => c.Currency).NotEmpty();
        RuleFor(c => c.Lines).NotEmpty();
    }
}

/// <summary>
/// Returns <see cref="Result{T}"/> and never throws for an expected failure. The pipeline
/// behaviors registered in Program.cs (tracing, logging, metrics, validation) wrap this
/// automatically — the handler itself stays free of cross-cutting concerns.
/// </summary>
public sealed class PlaceOrderHandler(IOrderRepository repository, IClock clock)
    : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var money = Money.Create(request.Amount, request.Currency);
        if (!money.IsValid)
            return Result<Guid>.Failure(Error.Validation(money.Errors));

        var placed = Order.Place(request.Customer, money.Value, request.Lines, clock);
        if (placed.IsFailure)
            return Result<Guid>.Failure(placed.Error);

        await repository.AddAsync(placed.Value, cancellationToken);
        return Result<Guid>.Success(placed.Value.Id.Value);
    }
}
