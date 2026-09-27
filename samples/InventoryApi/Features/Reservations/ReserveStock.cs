using SharedKernel.Application.Messaging;
using SharedKernel.Application.Validation;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace InventoryApi.Features.Reservations;

/// <summary>Reserves stock of a SKU. A repeated <paramref name="IdempotencyKey"/> returns the first reservation.</summary>
/// <param name="Sku">The SKU.</param>
/// <param name="Quantity">How many; positive.</param>
/// <param name="IdempotencyKey">The request's <c>Idempotency-Key</c>, when it has one.</param>
public sealed record ReserveStock(string Sku, int Quantity, string? IdempotencyKey) : ICommand<Reservation>;

public sealed class ReserveStockValidator : IRequestValidator<ReserveStock>
{
    public ValueTask<IReadOnlyList<Error>> ValidateAsync(ReserveStock request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<Error>>(request.Quantity > 0
            ? []
            : [Error.Validation("quantity", "Quantity must be positive.")]);
}

public sealed class ReserveStockHandler(InventoryStore inventory, IRequestContext caller) : ICommandHandler<ReserveStock, Reservation>
{
    public Task<Result<Reservation>> Handle(ReserveStock command, CancellationToken cancellationToken) =>
        Task.FromResult(inventory.Reserve(command.Sku, command.Quantity, caller.CorrelationId, command.IdempotencyKey));
}

/// <summary>One reservation.</summary>
/// <param name="Id">Its id.</param>
public sealed record GetReservation(Guid Id) : IQuery<Reservation>;

public sealed class GetReservationHandler(InventoryStore inventory) : IQueryHandler<GetReservation, Reservation>
{
    public Task<Result<Reservation>> Handle(GetReservation query, CancellationToken cancellationToken) =>
        Task.FromResult(inventory.GetReservation(query.Id));
}
