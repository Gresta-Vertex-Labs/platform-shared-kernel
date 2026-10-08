using SharedKernel.Application.Messaging;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Totp;
using Shop.Ordering.Application;
using Shop.Ordering.Infrastructure.Persistence;

namespace Shop.Ordering.Api;

/// <summary>
/// The body of a new order; the <c>Idempotency-Key</c> header is required. <c>PaymentToken</c> is the card the payment
/// provider tokenized in the customer's browser.
/// </summary>
public sealed record PlaceOrderRequest(
    string CustomerEmail,
    string ShippingAddress,
    string Currency,
    List<PlaceOrderLine> Lines,
    string PaymentToken
);

public sealed record OrderPlacedResponse(Guid Id);

/// <summary>A new authenticator enrollment: shown once, to be added to the user's app.</summary>
public sealed record TotpEnrollmentResponse(string SecretBase32, Uri ProvisioningUri);

public sealed record TotpCodeRequest(string Code);

/// <summary>What the database actually holds for an order, and its audit trail.</summary>
public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").RequireAuthorization();

        orders
            .MapPost(
                "/",
                (
                    PlaceOrderRequest body,
                    IdempotencyKey key,
                    ISender sender,
                    CancellationToken ct
                ) =>
                    sender
                        .Send(
                            new PlaceOrderCommand(
                                body.CustomerEmail,
                                body.ShippingAddress,
                                body.Currency,
                                body.Lines,
                                body.PaymentToken,
                                key.Value
                            ),
                            ct
                        )
                        .ToCreated(id => $"/orders/{id}", id => new OrderPlacedResponse(id))
            )
            .WithName("PlaceOrder");

        orders
            .MapGet(
                "/{id:guid}",
                (Guid id, ISender sender, CancellationToken ct) =>
                    sender.Send(new GetOrderQuery(id), ct).ToOk()
            )
            .WithName("GetOrder");

        // Cancelling releases stock and cannot be undone: it needs an authenticator step-up in the last five minutes.
        orders
            .MapPost(
                "/{id:guid}/cancel",
                (Guid id, ISender sender, CancellationToken ct) =>
                    sender.Send(new CancelOrderCommand(id), ct).ToNoContent()
            )
            .RequireAuthenticationMethod(TimeSpan.FromMinutes(5), "otp")
            .WithName("CancelOrder");

        var totp = app.MapGroup("/me/totp").RequireAuthorization();

        totp.MapPost(
            "/",
            async (
                IUserContext user,
                TotpEnrollmentService enrollments,
                ISender sender,
                CancellationToken ct
            ) =>
            {
                if (user.SubjectId is null)
                {
                    return Results.Forbid();
                }

                var enrollment = enrollments.Create(
                    "Shop",
                    user.Email ?? user.SubjectId,
                    recoveryCodeCount: 1
                );
                var saved = await sender.Send(
                    new SaveTotpEnrollmentCommand(user.SubjectId, enrollment.SecretBase32),
                    ct
                );
                return saved.IsSuccess
                    ? Results.Ok(
                        new TotpEnrollmentResponse(
                            enrollment.SecretBase32,
                            enrollment.ProvisioningUri
                        )
                    )
                    : Results.Problem(saved.Error.Message);
            }
        );

        totp.MapPost(
            "/verify",
            async (
                TotpCodeRequest body,
                IUserContext user,
                TotpChallengeService challenges,
                ISender sender,
                CancellationToken ct
            ) =>
            {
                if (user.SubjectId is null)
                {
                    return Results.Forbid();
                }

                var secret = await sender.Send(new GetTotpSecretQuery(user.SubjectId), ct);
                if (secret.IsFailure)
                {
                    return Results.NotFound();
                }

                var decoded = Base32.Decode(secret.Value);
                var outcome = await challenges.VerifyCodeAsync(
                    user,
                    decoded.Value,
                    body.Code,
                    cancellationToken: ct
                );
                return outcome == TotpChallengeResult.Verified
                    ? Results.NoContent()
                    : Results.BadRequest(outcome.ToString());
            }
        );

        // Operational view for the end-to-end tests: the columns as stored (ciphertext) and the order's audit actions.
        app.MapGet(
                "/ops/orders/{id:guid}/stored",
                async (
                    Guid id,
                    string? key,
                    OrderStorageInspector inspector,
                    CancellationToken ct
                ) =>
                    await inspector.ReadAsync(id, key, ct) is { } stored
                        ? Results.Ok(stored)
                        : Results.NotFound()
            )
            .RequireAuthorization();
    }
}
