using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-427: opt-in AES-GCM payload encryption. Order is always encrypt-then-sign — the
/// HMAC signature covers the transmitted ciphertext bytes, never the plaintext.
/// </summary>
public sealed class WebhookPayloadEncryptionTests
{
    private static WebhookSubscription Subscription(string secret) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), [secret], [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_EncryptPayloadEnabled_RoundTripsThroughEncryptThenSignThenVerifyThenDecrypt()
    {
        const string secret = "signing-secret";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var encryptionService = new AesGcmEncryptionService(new FakeEncryptionKeyProvider());

        string? capturedWireBody = null;
        string? capturedSignature = null;
        string? capturedTimestamp = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedWireBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedSignature = request.Headers.GetValues(WebhookSignatureHeaders.SignatureHeaderName).Single();
            capturedTimestamp = request.Headers.GetValues(WebhookSignatureHeaders.TimestampHeaderName).Single();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var harness = new WebhookTestHarness(
            handler,
            store,
            o =>
            {
                o.MaxAttempts = 1;
                o.EncryptPayload = true;
            },
            services => services.AddSingleton<ISymmetricEncryptionService>(encryptionService));

        var integrationEvent = new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, integrationEvent, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // The transmitted body is not readable plaintext JSON — it was encrypted before signing.
        capturedWireBody.Should().NotBeNullOrEmpty();
        capturedWireBody.Should().NotContain(integrationEvent.OrderId.ToString());

        // The signature verifies against the ciphertext that was actually transmitted...
        var signatureIsValid = WebhookSignatureVerifier.Verify(capturedWireBody, capturedTimestamp, capturedSignature, secret);
        signatureIsValid.Should().BeTrue();

        // ...and decrypting that same ciphertext, with the same AAD the dispatcher derived
        // (subscription id + the delivery id reproducible from the response's own DeliveryId /
        // the X-Webhook-Delivery-Id header), recovers the original plaintext JSON.
        var associatedData = WebhookPayloadAssociatedData.Build(subscription.SubscriptionId, result.DeliveryId);
        var decrypted = await encryptionService.DecryptToStringAsync(capturedWireBody!, associatedData);
        decrypted.IsSuccess.Should().BeTrue();
        decrypted.Value.Should().Contain(integrationEvent.OrderId.ToString());
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_EncryptPayloadEnabled_CiphertextDecryptedAgainstDifferentSubscriptionIdFails()
    {
        const string secret = "signing-secret";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var encryptionService = new AesGcmEncryptionService(new FakeEncryptionKeyProvider());

        string? capturedWireBody = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedWireBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var harness = new WebhookTestHarness(
            handler,
            store,
            o =>
            {
                o.MaxAttempts = 1;
                o.EncryptPayload = true;
            },
            services => services.AddSingleton<ISymmetricEncryptionService>(encryptionService));

        var integrationEvent = new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, integrationEvent, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // A captured ciphertext decrypted against a DIFFERENT subscription's AAD — same delivery id,
        // wrong subscription id — must fail authentication. This is the anti-cross-subscription-replay
        // guarantee the AAD binding exists to provide: an attacker who captured this ciphertext cannot
        // reuse it against a different subscription's out-of-band-known identity.
        var wrongSubscriptionAssociatedData = WebhookPayloadAssociatedData.Build(Guid.NewGuid(), result.DeliveryId);
        var decryptedWithWrongSubscription = await encryptionService.DecryptToStringAsync(capturedWireBody!, wrongSubscriptionAssociatedData);
        decryptedWithWrongSubscription.IsSuccess.Should().BeFalse();

        // The correct subscription id, paired with the correct delivery id, still succeeds.
        var correctAssociatedData = WebhookPayloadAssociatedData.Build(subscription.SubscriptionId, result.DeliveryId);
        var decryptedWithCorrectSubscription = await encryptionService.DecryptToStringAsync(capturedWireBody!, correctAssociatedData);
        decryptedWithCorrectSubscription.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_EncryptPayloadDisabled_WireFormatUnchanged()
    {
        const string secret = "signing-secret";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        string? capturedWireBody = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedWireBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var integrationEvent = new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, integrationEvent, CancellationToken.None);

        capturedWireBody.Should().NotBeNullOrEmpty();
        capturedWireBody.Should().Contain(integrationEvent.OrderId.ToString());
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_EncryptPayloadEnabledWithoutRegisteredService_FailsClearlyRatherThanSilently()
    {
        var subscription = Subscription("secret");
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 1;
            o.EncryptPayload = true;
        });

        var act = async () => await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ISymmetricEncryptionService*");
    }
}
