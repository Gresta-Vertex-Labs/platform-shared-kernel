using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Integration.Webhooks.Options;

/// <summary>
/// Configures retry, backoff, timeout, signature tolerance, and concurrency behavior for outbound
/// webhook delivery.
/// </summary>
/// <remarks>
/// Bound to configuration section <c>"SharedKernel:Integration:Webhooks"</c> and registered via
/// <c>SharedKernel.Configuration</c>'s <c>AddValidatedOptions&lt;TOptions&gt;(IConfigurationSection)</c>,
/// which calls <c>.Bind(section).ValidateDataAnnotations().ValidateOnStart()</c>. Mechanical bounds
/// (<see cref="MaxAttempts"/> &gt;= 1, <see cref="MaxConcurrentDeliveries"/> &gt;= 1) are plain
/// <see cref="RangeAttribute"/> annotations; the cross-field rule
/// (<see cref="MaxBackoffDelay"/> &gt;= <see cref="BaseBackoffDelay"/>) and the positive-<see cref="TimeSpan"/>
/// checks DataAnnotations attributes cannot express on their own are implemented via
/// <see cref="IValidatableObject.Validate"/>.
/// </remarks>
public sealed class WebhookDeliveryOptions : IValidatableObject
{
    /// <summary>The maximum number of HTTP attempts made per delivery. Defaults to 5.</summary>
    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The initial delay before the first retry. Defaults to 2 seconds.</summary>
    public TimeSpan BaseBackoffDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The maximum delay between retries. Defaults to 60 seconds.</summary>
    public TimeSpan MaxBackoffDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The per-attempt HTTP request timeout. Defaults to 10 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum allowed clock skew accepted by <c>WebhookSignatureVerifier.Verify</c> when no
    /// explicit tolerance is supplied at the call site. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan SignatureTolerance { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The maximum number of concurrent deliveries within a single <c>DispatchAsync</c> fan-out.
    /// Defaults to 8.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentDeliveries { get; set; } = 8;

    /// <summary>
    /// When <see langword="true"/>, disables the default <c>IWebhookUrlValidator</c>'s SSRF guard —
    /// loopback, link-local, private (RFC1918/RFC4193), and multicast/reserved targets are permitted
    /// through. Defaults to <see langword="false"/> (fail-closed). Intended for legitimate internal
    /// test/staging subscriptions only — never enable this for a service that accepts
    /// externally-supplied subscription URLs.
    /// </summary>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the outbound JSON payload is encrypted (AES-GCM, via
    /// <c>SharedKernel.Cryptography</c>'s <c>ISymmetricEncryptionService.EncryptToStringAsync</c>) before
    /// signing — encrypt-then-sign, so the HMAC signature continues to cover exactly the transmitted
    /// bytes. Defaults to <see langword="false"/>. TLS already provides transport confidentiality; this
    /// is defense-in-depth for subscribers who want payload-level confidentiality independent of their
    /// own TLS termination boundary. Requires an <c>ISymmetricEncryptionService</c> to be registered
    /// (via <c>SharedKernel.Cryptography</c>'s <c>AddSharedKernelCryptography(configuration).AddSymmetricEncryption()</c>
    /// plus a consumer-supplied <c>IEncryptionKeyProvider</c>) — enabling this option without registering that
    /// service fails loudly at first delivery, not silently. The transmitted body is the ciphertext's canonical
    /// <c>EncryptedPayload.ToString()</c> form (unpadded Base64Url), which a subscriber reads back with
    /// <c>EncryptedPayload.TryParse</c> or <c>ISymmetricEncryptionService.DecryptToStringAsync</c>.
    /// </summary>
    /// <remarks>
    /// The associated-data (AAD) bound into the AES-GCM authentication tag is always
    /// <see cref="Signing.WebhookPayloadAssociatedData.Build"/>, applied to the target subscription id
    /// and the per-delivery id — never a constant, and never derived solely from data transmitted on
    /// the wire. A subscriber decrypting the payload must derive the identical AAD itself: the
    /// subscription id is known only out-of-band (the same pre-established channel that already
    /// carries <see cref="Subscriptions.WebhookSubscription.Secrets"/>), and the delivery id is
    /// reproducible from the <see cref="Signing.WebhookSignatureHeaders.DeliveryIdHeaderName"/> header
    /// sent on every attempt. See <see cref="Signing.WebhookPayloadAssociatedData"/> for the full
    /// reasoning behind why the subscription id is never transmitted as a header.
    /// </remarks>
    public bool EncryptPayload { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseBackoffDelay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(BaseBackoffDelay)} must be greater than zero.",
                [nameof(BaseBackoffDelay)]);
        }

        if (MaxBackoffDelay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(MaxBackoffDelay)} must be greater than zero.",
                [nameof(MaxBackoffDelay)]);
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(RequestTimeout)} must be greater than zero.",
                [nameof(RequestTimeout)]);
        }

        if (SignatureTolerance <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(SignatureTolerance)} must be greater than zero.",
                [nameof(SignatureTolerance)]);
        }

        if (BaseBackoffDelay > TimeSpan.Zero && MaxBackoffDelay > TimeSpan.Zero && MaxBackoffDelay < BaseBackoffDelay)
        {
            yield return new ValidationResult(
                $"{nameof(MaxBackoffDelay)} ({MaxBackoffDelay}) must be greater than or equal to {nameof(BaseBackoffDelay)} ({BaseBackoffDelay}).",
                [nameof(MaxBackoffDelay), nameof(BaseBackoffDelay)]);
        }
    }
}
