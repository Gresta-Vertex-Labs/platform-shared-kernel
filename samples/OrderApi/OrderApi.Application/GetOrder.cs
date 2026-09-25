using OrderApi.Domain;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace OrderApi.Application;

public sealed record OrderDto(Guid Id, string Customer, decimal Amount, string Currency, IReadOnlyList<string> Lines);

public sealed record GetOrderQuery(Guid Id) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IOrderRepository repository) : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await repository.GetAsync(request.Id, cancellationToken);

        // Error.NotFound maps to HTTP 404 automatically at the boundary — the handler
        // never decides on a status code.
        return order is null
            ? Result<OrderDto>.Failure(Error.NotFound("order.notFound", $"Order {request.Id} was not found."))
            : Result<OrderDto>.Success(new OrderDto(
                order.Id.Value, order.Customer, order.Total.Amount, order.Total.Currency, order.Lines));
    }
}
