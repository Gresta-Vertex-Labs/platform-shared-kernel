using SharedKernel.Application.Messaging;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.Execution.Context;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Clocks;
using Shop.Billing.Api.Payments;
using Shop.Billing.Api.Privacy;
using Shop.Billing.Api.Profiles;
using Shop.Contracts.Billing;

namespace Shop.Billing.Api;

public sealed record SaveBillingProfileRequest(
    string LegalName,
    string Country,
    string VatNumber,
    string Iban,
    string? Bic
);

public sealed record DisputeNotice(Guid PaymentId);

public sealed record DataSubjectRequestBody(string Email);

public sealed class BillingEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var payments = app.MapGroup("/payments").RequireAuthorization();

        // Ordering's fulfilment workflow takes payment here (an API key, acting for the order's tenant).
        payments.MapPost(
            "/",
            async (
                ChargeRequest body,
                ISender sender,
                PaymentAnnouncements announcements,
                CancellationToken ct
            ) =>
            {
                var charged = await sender.Send(new ChargeCommand(body), ct);
                if (charged.IsSuccess)
                {
                    // After the commit: the merchant hears only of payments that exist.
                    await announcements.AnnounceCapturedAsync(charged.Value, ct);
                }

                return charged.ToOk();
            }
        );

        payments.MapPost(
            "/by-order/{orderId:guid}/refund",
            (Guid orderId, ISender sender, CancellationToken ct) =>
                sender.Send(new RefundOrderPaymentCommand(orderId), ct).ToOk()
        );

        payments.MapGet(
            "/by-order/{orderId:guid}",
            (Guid orderId, ISender sender, CancellationToken ct) =>
                sender.Send(new GetOrderPaymentQuery(orderId), ct).ToOk()
        );

        payments.MapGet(
            "/{paymentId:guid}/invoice",
            (Guid paymentId, ISender sender, CancellationToken ct) =>
                sender.Send(new GetInvoiceQuery(paymentId), ct).ToOk()
        );

        // The payment provider's callback: an API key bound to the merchant's tenant, gated at the edge too.
        app.MapPost(
                "/provider/disputes",
                (DisputeNotice body, ISender sender, CancellationToken ct) =>
                    sender.Send(new RecordDisputeCommand(body.PaymentId), ct).ToOk()
            )
            .RequireAuthorization()
            .RequireEndpointPermission(BillingPermissions.Callback);

        var profile = app.MapGroup("/billing-profile").RequireAuthorization();
        profile.MapPut(
            "/",
            (SaveBillingProfileRequest body, ISender sender, CancellationToken ct) =>
                sender
                    .Send(
                        new SaveBillingProfileCommand(
                            body.LegalName,
                            body.Country,
                            body.VatNumber,
                            body.Iban,
                            body.Bic
                        ),
                        ct
                    )
                    .ToNoContent()
        );
        profile.MapGet(
            "/",
            (ISender sender, CancellationToken ct) =>
                sender.Send(new GetBillingProfileQuery(), ct).ToOk()
        );

        // GDPR access and erasure for one customer of the caller's tenant.
        var privacy = app.MapGroup("/privacy")
            .RequireAuthorization()
            .RequireEndpointPermission(BillingPermissions.Manage);
        privacy.MapPost(
            "/export",
            async (
                DataSubjectRequestBody body,
                IDataSubjectRequestHandler handler,
                IRequestContext caller,
                IClock clock,
                CancellationToken ct
            ) =>
                (await handler.ExportAsync(Request(body, caller, clock), ct)).ToOk(export =>
                    export.Records.Select(r => r.Data).ToList()
                )
        );
        privacy.MapPost(
            "/erase",
            async (
                DataSubjectRequestBody body,
                IDataSubjectRequestHandler handler,
                IRequestContext caller,
                IClock clock,
                CancellationToken ct
            ) =>
                (await handler.EraseAsync(Request(body, caller, clock), ct)).ToOk(receipt => new
                {
                    receipt.AnonymizedRecords,
                    receipt.IsComplete,
                })
        );
    }

    private static DataSubjectRequest Request(
        DataSubjectRequestBody body,
        IRequestContext caller,
        IClock clock
    ) =>
        new(
            Guid.CreateVersion7().ToString("D"),
            body.Email,
            clock.UtcNow,
            caller.TenantId?.Value.ToString("D")
        );
}
