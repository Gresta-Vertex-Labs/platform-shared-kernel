using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Integration.Webhooks.Signing;

/// <summary>
/// Computes the HMAC-SHA256 signature for an outbound webhook delivery.
/// </summary>
/// <remarks>
/// Stateless and thread-safe — registered as a singleton by <c>AddSharedKernelWebhooks</c>. Built
/// entirely on BCL primitives (<see cref="HMACSHA256"/>), so it is fully AOT-compatible with no
/// reflection involved.
/// </remarks>
public sealed class WebhookSignatureProvider
{
    /// <summary>
    /// Computes the HMAC-SHA256 signature for a webhook payload at a given point in time.
    /// </summary>
    /// <param name="payloadJson">The serialized JSON payload that will be sent on the wire.</param>
    /// <param name="secret">
    /// The subscription's shared secret. Used only as the HMAC key — never transmitted, logged, or
    /// otherwise surfaced; only the returned digest is.
    /// </param>
    /// <param name="timestamp">
    /// The timestamp to prefix onto the signing input, as Unix seconds. The receiving side must use
    /// the identical value (carried in <see cref="WebhookSignatureHeaders.TimestampHeaderName"/>) to
    /// reproduce the same digest.
    /// </param>
    /// <returns>The lowercase hexadecimal HMAC-SHA256 digest.</returns>
    /// <remarks>
    /// The signing input is always <c>"{unixSeconds}.{payloadJson}"</c>, UTF-8 encoded — never the
    /// payload alone. Signing the payload alone would provide authenticity but not freshness,
    /// allowing a captured request to be replayed indefinitely. Freshness is enforced separately by
    /// <see cref="WebhookSignatureVerifier"/>'s tolerance window.
    /// </remarks>
    public string Sign(string payloadJson, string secret, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentNullException.ThrowIfNull(secret);

        var signingInput = BuildSigningInput(timestamp.ToUnixTimeSeconds(), payloadJson);
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var inputBytes = Encoding.UTF8.GetBytes(signingInput);

        var digest = HMACSHA256.HashData(keyBytes, inputBytes);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>Builds the canonical signing input shared by signing and verification.</summary>
    internal static string BuildSigningInput(long unixSeconds, string payloadJson) =>
        $"{unixSeconds}.{payloadJson}";
}
