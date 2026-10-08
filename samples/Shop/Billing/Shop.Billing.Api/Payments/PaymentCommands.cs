using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Billing.Api.Persistence;
using Shop.Contracts.Billing;

namespace Shop.Billing.Api.Payments;

public static class BillingPermissions
{
    /// <summary>Take payment for an order (the Ordering service).</summary>
    public const string Charge = "payments.charge";

    /// <summary>Refund an order's payment (the Ordering service).</summary>
    public const string Refund = "payments.refund";

    /// <summary>Report payment events (the payment provider).</summary>
    public const string Callback = "payments.callback";

    /// <summary>Read payments and invoices (merchants).</summary>
    public const string Read = "billing.read";

    /// <summary>Manage the billing profile and data-subject requests (merchants).</summary>
    public const string Manage = "billing.manage";
}

internal static class Caller
{
    public static Result<TenantId> Tenant(IRequestContext caller) =>
        caller.TenantId is { } tenant
            ? Result<TenantId>.Success(tenant)
            : Result<TenantId>.Failure(
                Error.Forbidden("billing.tenant_required", "Billing needs a caller with a tenant.")
            );
}

/// <summary>
/// Takes payment for an order through the (stand-in) payment provider and issues its signed invoice. Safe to retry: a
/// second charge for the same order returns the first payment.
/// </summary>
[RequirePermission(BillingPermissions.Charge)]
public sealed record ChargeCommand(ChargeRequest Request) : ICommand<PaymentView>;

public sealed class ChargeCommandValidator : AbstractValidator<ChargeCommand>
{
    public ChargeCommandValidator()
    {
        RuleFor(c => c.Request.OrderId).NotEmpty();
        RuleFor(c => c.Request.Amount).GreaterThan(0);
        RuleFor(c => c.Request.Currency).NotEmpty().Length(3);
        RuleFor(c => c.Request.CustomerEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Request.PaymentToken).NotEmpty();
    }
}

public sealed class ChargeHandler(
    IRequestContext caller,
    BillingDbContext db,
    InvoiceIssuer invoices,
    IEventPublisher events,
    IClock clock
) : ICommandHandler<ChargeCommand, PaymentView>
{
    public async Task<Result<PaymentView>> Handle(ChargeCommand command, CancellationToken ct)
    {
        var tenant = Caller.Tenant(caller);
        if (tenant.IsFailure)
        {
            return Result<PaymentView>.Failure(tenant.Error);
        }

        var request = command.Request;
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == request.OrderId, ct);
        if (existing is not null)
        {
            return Result<PaymentView>.Success(existing.ToView());
        }

        // The stand-in provider: one token always captures, one always declines.
        if (request.PaymentToken == PaymentTokens.Declined)
        {
            return Result<PaymentView>.Failure(
                Error.BusinessRule(BillingErrorCodes.PaymentDeclined, "The card was declined.")
            );
        }

        if (request.PaymentToken != PaymentTokens.Approved)
        {
            return Result<PaymentView>.Failure(
                Error.Validation("billing.payment_token.unknown", "Unknown payment token.")
            );
        }

        var currency = Currency.Create(request.Currency);
        if (!currency.IsValid)
        {
            return Result<PaymentView>.Failure(Error.Validation(currency.Errors));
        }

        var amount = Money.Create(request.Amount, currency.Value);
        if (!amount.IsValid)
        {
            return Result<PaymentView>.Failure(Error.Validation(amount.Errors));
        }

        var payment = Payment.Capture(
            tenant.Value,
            request.OrderId,
            amount.Value,
            request.CustomerEmail,
            $"ch_{Guid.NewGuid():N}",
            clock
        );
        var seller = await db.Profiles.FirstOrDefaultAsync(ct);
        var issued = await invoices.IssueAsync(payment, seller, ct);
        if (issued.IsFailure)
        {
            return Result<PaymentView>.Failure(issued.Error);
        }

        db.Payments.Add(payment);

        // Written to the outbox in this transaction; RabbitMQ gets it after the commit, so a receipt is owed
        // exactly when the payment exists. The payment's id is the event's: a redelivery repeats the same event.
        var published = await events.PublishAsync(
            new ReceiptDue(
                payment.Id.Value,
                clock.UtcNow,
                payment.Id.Value,
                payment.OrderId,
                request.CustomerEmail,
                payment.Amount.Amount,
                payment.Amount.Currency.Code
            ),
            ct
        );
        return published.IsFailure
            ? Result<PaymentView>.Failure(published.Error)
            : Result<PaymentView>.Success(payment.ToView());
    }
}

/// <summary>Refunds an order's payment; an order that was never paid has nothing to refund.</summary>
[RequirePermission(BillingPermissions.Refund)]
public sealed record RefundOrderPaymentCommand(Guid OrderId) : ICommand<PaymentView>;

public sealed class RefundOrderPaymentHandler(BillingDbContext db)
    : ICommandHandler<RefundOrderPaymentCommand, PaymentView>
{
    public async Task<Result<PaymentView>> Handle(
        RefundOrderPaymentCommand command,
        CancellationToken ct
    )
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == command.OrderId, ct);
        if (payment is null)
        {
            return Result<PaymentView>.Failure(PaymentErrors.NotFound);
        }

        var refunded = payment.Refund();
        return refunded.IsFailure
            ? Result<PaymentView>.Failure(refunded.Error)
            : Result<PaymentView>.Success(payment.ToView());
    }
}

/// <summary>The payment provider reports that the customer's bank disputes a payment.</summary>
[RequirePermission(BillingPermissions.Callback)]
public sealed record RecordDisputeCommand(Guid PaymentId) : ICommand<PaymentView>;

public sealed class RecordDisputeHandler(BillingDbContext db)
    : ICommandHandler<RecordDisputeCommand, PaymentView>
{
    public async Task<Result<PaymentView>> Handle(
        RecordDisputeCommand command,
        CancellationToken ct
    )
    {
        var payment = await db.Payments.FirstOrDefaultAsync(
            p => p.Id == new PaymentId(command.PaymentId),
            ct
        );
        if (payment is null)
        {
            return Result<PaymentView>.Failure(PaymentErrors.NotFound);
        }

        var disputed = payment.Dispute();
        return disputed.IsFailure
            ? Result<PaymentView>.Failure(disputed.Error)
            : Result<PaymentView>.Success(payment.ToView());
    }
}

[RequirePermission(BillingPermissions.Read)]
public sealed record GetOrderPaymentQuery(Guid OrderId) : IQuery<PaymentView>;

public sealed class GetOrderPaymentHandler(BillingDbContext db)
    : IQueryHandler<GetOrderPaymentQuery, PaymentView>
{
    public async Task<Result<PaymentView>> Handle(GetOrderPaymentQuery query, CancellationToken ct)
    {
        var payment = await db
            .Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == query.OrderId, ct);
        return payment is null
            ? Result<PaymentView>.Failure(PaymentErrors.NotFound)
            : Result<PaymentView>.Success(payment.ToView());
    }
}

[RequirePermission(BillingPermissions.Read)]
public sealed record GetInvoiceQuery(Guid PaymentId) : IQuery<InvoiceView>;

public sealed class GetInvoiceHandler(BillingDbContext db, InvoiceIssuer invoices)
    : IQueryHandler<GetInvoiceQuery, InvoiceView>
{
    public async Task<Result<InvoiceView>> Handle(GetInvoiceQuery query, CancellationToken ct)
    {
        var payment = await db
            .Payments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == new PaymentId(query.PaymentId), ct);
        return payment is null
            ? Result<InvoiceView>.Failure(PaymentErrors.NotFound)
            : await invoices.ReadAsync(payment, ct);
    }
}

public static class PaymentErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "billing.payment.not_found",
        "No such payment."
    );
}

internal static class PaymentViews
{
    public static PaymentView ToView(this Payment payment) =>
        new(
            payment.Id.Value,
            payment.OrderId,
            payment.Status.ToString(),
            payment.Amount.Amount,
            payment.Amount.Currency.Code
        );
}
