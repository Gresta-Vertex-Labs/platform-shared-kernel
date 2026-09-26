using OrderApi.Domain;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Application.Features.Orders;

/// <summary>Cancels an order.</summary>
/// <remarks>
/// The permission is declared once, here, on the use case — never on the endpoint. The pipeline's authorization
/// behavior checks it against <c>IRequestContext</c> before the handler runs, on every path the command is sent from
/// (HTTP today, a message consumer or a job tomorrow): an anonymous caller gets <c>Error.Unauthorized</c>
/// (<c>unauthorized.default</c>, HTTP 401), a caller without the permission <c>Error.Forbidden</c>
/// (<c>forbidden.insufficient_permission</c>, HTTP 403).
/// </remarks>
/// <param name="Id">The id of the order to cancel.</param>
[RequirePermission(OrderPermissions.Cancel)]
public sealed record CancelOrderCommand(Guid Id) : ICommand;

/// <summary>Loads the order, cancels it through the aggregate, and stores it.</summary>
public sealed class CancelOrderHandler(IOrderRepository repository) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetAsync(request.Id, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("order.notFound", $"Order {request.Id} was not found."));

        var cancelled = order.Cancel();
        if (cancelled.IsFailure)
            return cancelled;

        await repository.UpdateAsync(order, cancellationToken);
        return Result.Success();
    }
}
