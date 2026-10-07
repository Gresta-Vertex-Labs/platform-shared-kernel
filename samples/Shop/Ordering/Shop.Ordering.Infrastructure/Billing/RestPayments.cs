using SharedKernel.Communication;
using SharedKernel.Primitives.Results;
using Shop.Contracts.Billing;
using Shop.Ordering.Application;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Infrastructure.Billing;

/// <summary>
/// Billing over REST. The client is the kernel's (11.Communication): the base address, the API key, the caller's tenant
/// and correlation id on every request, retries and timeouts from configuration, and a ProblemDetails answer mapped back
/// to the matching <c>Error</c> — so a declined card arrives here as Billing's business-rule failure.
/// </summary>
public sealed class RestPayments(HttpClient http) : IPayments
{
    public const string ClientName = "billing";

    public async Task<Result<Guid>> ChargeAsync(PaymentRequest request, CancellationToken ct)
    {
        var charged = await http.PostResultAsync(
            "payments",
            new ChargeRequest(
                request.OrderId.Value,
                request.Amount,
                request.Currency,
                request.CustomerEmail,
                request.PaymentToken
            ),
            BillingJsonContext.Default.ChargeRequest,
            BillingJsonContext.Default.PaymentView,
            ct
        );
        return charged.IsFailure
            ? Result<Guid>.Failure(charged.Error)
            : Result<Guid>.Success(charged.Value.PaymentId);
    }

    public async Task<Result> RefundAsync(OrderId orderId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"payments/by-order/{orderId.Value:D}/refund"
        );
        return await http.SendResultAsync(request, ct);
    }
}
