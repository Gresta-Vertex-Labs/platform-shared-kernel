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

        // ...and decrypting that same ciphertext recovers the original plaintext JSON.
        var decrypted = encryptionService.DecryptToString(capturedWireBody!);
        decrypted.IsSuccess.Should().BeTrue();
        decrypted.Value.Should().Contain(integrationEvent.OrderId.ToString());
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
