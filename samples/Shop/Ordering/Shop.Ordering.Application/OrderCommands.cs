using System.Globalization;
using FluentValidation;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Contracts.Ordering;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Application;

/// <summary>A line as the customer orders it.</summary>
public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// Places an order. Idempotent per <c>Idempotency-Key</c> (a retried request replays the first answer), audited, and
/// announced as <see cref="OrderPlaced"/> through the outbox in the same transaction.
/// </summary>
[RequirePermission(OrderingPermissions.Place)]
public sealed record PlaceOrderCommand(
    string CustomerEmail,
    string ShippingAddress,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines,
    string PaymentToken,
    string IdempotencyKey
) : ICommand<Guid>, IIdempotentRequest, IAuditableRequest<Result<Guid>>
{
    public string Action => "order.placed";

    public string ResourceType => nameof(Order);

    // The order does not exist yet; the after-snapshot names it.
    public string ResourceId => IdempotencyKey;

    public string? BeforeSnapshot => null;

    public string? GetAfterSnapshot(Result<Guid> response) =>
        response.IsSuccess ? response.Value.ToString("D", CultureInfo.InvariantCulture) : null;
}

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(c => c.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.ShippingAddress).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Lines).NotEmpty();
        RuleFor(c => c.PaymentToken).NotEmpty().MaximumLength(64);
        RuleForEach(c => c.Lines)
            .ChildRules(line =>
            {
                line.RuleFor(l => l.Sku).NotEmpty();
                line.RuleFor(l => l.Quantity).GreaterThan(0);
                line.RuleFor(l => l.UnitPrice).GreaterThan(0);
            });
    }
}

public sealed class PlaceOrderHandler(
    IRequestContext caller,
    IRepository<Order, OrderId> orders,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<PlaceOrderCommand, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        var tenant = CallerTenant.Of(caller);
        if (tenant.IsFailure)
        {
            return Result<Guid>.Failure(tenant.Error);
        }

        var currency = Currency.Create(command.Currency);
        if (!currency.IsValid)
        {
            return Result<Guid>.Failure(Error.Validation(currency.Errors));
        }

        var lines = new List<(string, int, Money)>();
        foreach (var line in command.Lines)
        {
            var price = Money.Create(line.UnitPrice, currency.Value);
            if (!price.IsValid)
            {
                return Result<Guid>.Failure(Error.Validation(price.Errors));
            }

            lines.Add((line.Sku, line.Quantity, price.Value));
        }

        var placed = Order.Place(
            tenant.Value,
            command.CustomerEmail,
            command.ShippingAddress,
            lines,
            clock
        );
        if (!placed.IsValid)
        {
            return Result<Guid>.Failure(Error.Validation(placed.Errors));
        }

        var order = placed.Value;
        await orders.AddAsync(order, ct);

        // Written to the outbox in this transaction; delivered after the commit.
        var published = await events.PublishAsync(
            new OrderPlaced(
                Guid.CreateVersion7(),
                clock.UtcNow,
                order.Id.Value,
                [
                    .. order.Lines.Select(l => new OrderLineContract(
                        l.Sku,
                        l.Quantity,
                        l.UnitPrice.Amount
                    )),
                ],
                order.Total.Amount,
                order.Total.Currency.Code,
                command.PaymentToken
            ),
            ct
        );
        return published.IsFailure
            ? Result<Guid>.Failure(published.Error)
            : Result<Guid>.Success(order.Id.Value);
    }
}

/// <summary>Fulfilment held the stock: confirm the order. Sent by the fulfilment workflow.</summary>
public sealed record ConfirmOrderCommand(Guid OrderId, Guid ReservationId)
    : ICommand,
        IAuditableRequest<Result>
{
    public string Action => "order.confirmed";

    public string ResourceType => nameof(Order);

    public string ResourceId => OrderId.ToString("D", CultureInfo.InvariantCulture);

    public string? BeforeSnapshot => null;

    public string? GetAfterSnapshot(Result response) => null;
}

public sealed class ConfirmOrderHandler(
    IRepository<Order, OrderId> orders,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<ConfirmOrderCommand>
{
    public async Task<Result> Handle(ConfirmOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), ct);
        if (order is null)
        {
            return Result.Failure(
                Error.NotFound(
                    "ordering.order.not_found",
                    $"Order {command.OrderId} was not found."
                )
            );
        }

        bool already = order.Status == OrderStatus.Confirmed;
        var confirmed = order.Confirm(command.ReservationId);
        if (confirmed.IsFailure || already)
        {
            return confirmed;
        }

        return await events.PublishAsync(
            new OrderConfirmed(
                Guid.CreateVersion7(),
                clock.UtcNow,
                order.Id.Value,
                command.ReservationId,
                order.Total.Amount,
                order.Total.Currency.Code
            ),
            ct
        );
    }
}

/// <summary>Fulfilment refused the order. Sent by the fulfilment workflow.</summary>
public sealed record RejectOrderCommand(Guid OrderId, string Reason) : ICommand;

public sealed class RejectOrderHandler(
    IRepository<Order, OrderId> orders,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<RejectOrderCommand>
{
    public async Task<Result> Handle(RejectOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), ct);
        if (order is null)
        {
            return Result.Failure(
                Error.NotFound(
                    "ordering.order.not_found",
                    $"Order {command.OrderId} was not found."
                )
            );
        }

        bool already = order.Status == OrderStatus.Rejected;
        var rejected = order.Reject(command.Reason);
        if (rejected.IsFailure || already)
        {
            return rejected;
        }

        return await events.PublishAsync(
            new OrderRejected(Guid.CreateVersion7(), clock.UtcNow, order.Id.Value, command.Reason),
            ct
        );
    }
}

/// <summary>
/// The customer cancels a confirmed order: the payment is refunded, the stock released and the cancellation
/// announced. The endpoint also requires a fresh authenticator-app step-up.
/// </summary>
[RequirePermission(OrderingPermissions.Cancel)]
public sealed record CancelOrderCommand(Guid OrderId) : ICommand, IAuditableRequest<Result>
{
    public string Action => "order.cancelled";

    public string ResourceType => nameof(Order);

    public string ResourceId => OrderId.ToString("D", CultureInfo.InvariantCulture);

    public string? BeforeSnapshot => null;

    public string? GetAfterSnapshot(Result response) => null;
}

public sealed class CancelOrderHandler(
    IRepository<Order, OrderId> orders,
    IInventoryReservations inventory,
    IPayments payments,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), ct);
        if (order is null)
        {
            return Result.Failure(
                Error.NotFound(
                    "ordering.order.not_found",
                    $"Order {command.OrderId} was not found."
                )
            );
        }

        var cancelled = order.Cancel();
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        var refunded = await payments.RefundAsync(order.Id, ct);
        if (refunded.IsFailure)
        {
            return refunded;
        }

        var released = await inventory.ReleaseAsync(order.Id, ct);
        if (released.IsFailure)
        {
            return Result.Failure(released.Error);
        }

        return await events.PublishAsync(
            new OrderCancelled(Guid.CreateVersion7(), clock.UtcNow, order.Id.Value),
            ct
        );
    }
}

/// <summary>An order as its customer sees it.</summary>
public sealed record OrderDto(
    Guid Id,
    string Status,
    string CustomerEmail,
    string ShippingAddress,
    decimal Total,
    string Currency,
    Guid? ReservationId,
    string? RejectionReason,
    IReadOnlyList<PlaceOrderLine> Lines
);

[RequirePermission(OrderingPermissions.Read)]
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderHandler(IReadRepository<Order, OrderId> orders)
    : IQueryHandler<GetOrderQuery, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(query.OrderId), ct);
        return order is null
            ? Result<OrderDto>.Failure(
                Error.NotFound("ordering.order.not_found", $"Order {query.OrderId} was not found.")
            )
            : Result<OrderDto>.Success(
                new OrderDto(
                    order.Id.Value,
                    order.Status.ToString(),
                    order.CustomerEmail,
                    order.ShippingAddress,
                    order.Total.Amount,
                    order.Total.Currency.Code,
                    order.ReservationId,
                    order.RejectionReason,
                    [
                        .. order.Lines.Select(l => new PlaceOrderLine(
                            l.Sku,
                            l.Quantity,
                            l.UnitPrice.Amount
                        )),
                    ]
                )
            );
    }
}

/// <summary>
/// Takes payment for a placed order through Billing. Sent by the fulfilment workflow once the stock is held; a declined
/// card fails as a business rule, which the workflow answers by releasing the stock and rejecting the order.
/// </summary>
public sealed record ChargeOrderCommand(Guid OrderId, string PaymentToken) : ICommand;

public sealed class ChargeOrderHandler(IReadRepository<Order, OrderId> orders, IPayments payments)
    : ICommandHandler<ChargeOrderCommand>
{
    public async Task<Result> Handle(ChargeOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(new OrderId(command.OrderId), ct);
        if (order is null)
        {
            return Result.Failure(
                Error.NotFound(
                    "ordering.order.not_found",
                    $"Order {command.OrderId} was not found."
                )
            );
        }

        var charged = await payments.ChargeAsync(
            new PaymentRequest(
                order.Id,
                order.Total.Amount,
                order.Total.Currency.Code,
                order.CustomerEmail,
                command.PaymentToken
            ),
            ct
        );
        return charged.IsFailure ? Result.Failure(charged.Error) : Result.Success();
    }
}

/// <summary>The workflow's compensation when payment fails: give the held stock back.</summary>
public sealed record ReleaseOrderStockCommand(Guid OrderId) : ICommand;

public sealed class ReleaseOrderStockHandler(IInventoryReservations inventory)
    : ICommandHandler<ReleaseOrderStockCommand>
{
    public async Task<Result> Handle(ReleaseOrderStockCommand command, CancellationToken ct)
    {
        var released = await inventory.ReleaseAsync(new OrderId(command.OrderId), ct);
        return released.IsFailure ? Result.Failure(released.Error) : Result.Success();
    }
}

/// <summary>Pushes every status change to connected clients.</summary>
public sealed class NotifyOrderStatus(IOrderStatusNotifier notifier)
    : IDomainEventHandler<OrderStatusChanged>
{
    public Task Handle(OrderStatusChanged domainEvent, CancellationToken cancellationToken) =>
        notifier.NotifyAsync(
            new OrderStatusNotice(
                domainEvent.OrderId.Value,
                domainEvent.TenantId.Value,
                domainEvent.Status.ToString()
            ),
            cancellationToken
        );
}
