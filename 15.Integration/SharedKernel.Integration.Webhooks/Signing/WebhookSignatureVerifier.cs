using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Integration.Webhooks.Signing;

/// <summary>
/// Verifies an inbound webhook's HMAC-SHA256 signature against its claimed timestamp and payload.
/// </summary>
/// <remarks>
/// This is the primitive a downstream service's inbound webhook receiver endpoint (typically a
/// <c>14.Presentation</c> Minimal API route) calls to validate a webhook claiming to originate from a
/// <c>WebhookDispatcher</c>-style dispatcher elsewhere on the platform. Stateless, BCL-only,
/// fully AOT-compatible.
/// </remarks>
public static class WebhookSignatureVerifier
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Verifies that <paramref name="signatureHeaderValue"/> is a valid HMAC-SHA256 signature of
    /// <paramref name="payloadJson"/> under <paramref name="secret"/>, signed at
    /// <paramref name="timestampHeaderValue"/>, and that the timestamp falls within
    /// <paramref name="tolerance"/> of the current time.
    /// </summary>
    /// <param name="payloadJson">The raw JSON payload exactly as received on the wire.</param>
    /// <param name="timestampHeaderValue">
    /// The value of <see cref="WebhookSignatureHeaders.TimestampHeaderName"/> as received.
    /// </param>
    /// <param name="signatureHeaderValue">
    /// The value of <see cref="WebhookSignatureHeaders.SignatureHeaderName"/> as received.
    /// </param>
    /// <param name="secret">The subscription's shared secret used to recompute the expected digest.</param>
    /// <param name="tolerance">
    /// The maximum allowed clock skew between the signed timestamp and now. Defaults to 5 minutes
    /// when not supplied.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the signature is valid and the timestamp is within tolerance;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Never throws. A malformed timestamp, a malformed or missing signature, or a digest mismatch
    /// all produce <see langword="false"/>. Digest comparison uses
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> exclusively — never <c>==</c> or
    /// <see cref="string.Equals(string)"/> on the digest, which would be a timing-attack vulnerability.
    /// </remarks>
    public static bool Verify(
        string? payloadJson,
        string? timestampHeaderValue,
        string? signatureHeaderValue,
        string? secret,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(payloadJson) ||
            string.IsNullOrEmpty(timestampHeaderValue) ||
            string.IsNullOrEmpty(signatureHeaderValue) ||
            string.IsNullOrEmpty(secret))
        {
            return false;
        }

        if (!long.TryParse(timestampHeaderValue, out var unixSeconds))
        {
            return false;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var effectiveTolerance = tolerance ?? DefaultTolerance;
        if (effectiveTolerance < TimeSpan.Zero)
        {
            return false;
        }

        var skew = DateTimeOffset.UtcNow - signedAt;
        if (skew.Duration() > effectiveTolerance)
        {
            return false;
        }

        byte[] expectedDigest;
        byte[] receivedDigest;
        try
        {
            var signingInput = WebhookSignatureProvider.BuildSigningInput(unixSeconds, payloadJson);
            expectedDigest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingInput));
            receivedDigest = Convert.FromHexString(signatureHeaderValue);
        }
        catch (FormatException)
        {
            // signatureHeaderValue was not valid hex.
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedDigest, receivedDigest);
    }
}
