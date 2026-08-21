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
    /// Delegates to the <see cref="Verify(string?,string?,string?,IReadOnlyList{string}?,TimeSpan?)"/>
    /// overload with a one-element candidate list — retained unchanged for pre-rotation (v1.0.0) callers.
    /// </remarks>
    public static bool Verify(
        string? payloadJson,
        string? timestampHeaderValue,
        string? signatureHeaderValue,
        string? secret,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        return Verify(payloadJson, timestampHeaderValue, signatureHeaderValue, [secret], tolerance);
    }

    /// <summary>
    /// Verifies that <paramref name="signatureHeaderValue"/> is a valid HMAC-SHA256 signature of
    /// <paramref name="payloadJson"/> under <b>any</b> candidate in <paramref name="secretCandidates"/>,
    /// signed at <paramref name="timestampHeaderValue"/>, and that the timestamp falls within
    /// <paramref name="tolerance"/> of the current time.
    /// </summary>
    /// <param name="payloadJson">The raw JSON payload exactly as received on the wire.</param>
    /// <param name="timestampHeaderValue">
    /// The value of <see cref="WebhookSignatureHeaders.TimestampHeaderName"/> as received.
    /// </param>
    /// <param name="signatureHeaderValue">
    /// The value of <see cref="WebhookSignatureHeaders.SignatureHeaderName"/> as received.
    /// </param>
    /// <param name="secretCandidates">
    /// Every secret currently considered valid for the subscription — typically
    /// <c>WebhookSubscription.Secrets</c>, supporting zero-downtime signing-secret rotation (a
    /// dispatcher always signs with the newest secret, but a subscriber accepts a signature produced
    /// with any still-active secret during the rotation overlap window).
    /// </param>
    /// <param name="tolerance">
    /// The maximum allowed clock skew between the signed timestamp and now. Defaults to 5 minutes
    /// when not supplied.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the signature matches any candidate and the timestamp is within
    /// tolerance; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Never throws — identical malformed-input handling to the single-secret overload. Evaluates
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> against <b>every</b> candidate in
    /// <paramref name="secretCandidates"/> without short-circuiting the iteration — the boolean
    /// result is accumulated across the full list, never returned early on the first match — so
    /// total comparison time cannot itself leak which rotation-window secret matched.
    /// </remarks>
    public static bool Verify(
        string? payloadJson,
        string? timestampHeaderValue,
        string? signatureHeaderValue,
        IReadOnlyList<string>? secretCandidates,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrEmpty(payloadJson) ||
            string.IsNullOrEmpty(timestampHeaderValue) ||
            string.IsNullOrEmpty(signatureHeaderValue) ||
            secretCandidates is null ||
            secretCandidates.Count == 0)
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

        byte[] receivedDigest;
        try
        {
            receivedDigest = Convert.FromHexString(signatureHeaderValue);
        }
        catch (FormatException)
        {
            // signatureHeaderValue was not valid hex.
            return false;
        }

        var signingInput = WebhookSignatureProvider.BuildSigningInput(unixSeconds, payloadJson);
        var inputBytes = Encoding.UTF8.GetBytes(signingInput);

        var isValid = false;
        foreach (var candidate in secretCandidates)
        {
            var expectedDigest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(candidate ?? string.Empty), inputBytes);

            // Deliberately not short-circuited (no `if (isValid) continue;`/early return) — every
            // candidate's FixedTimeEquals is evaluated so total comparison time does not itself leak
            // which rotation-window secret matched.
            isValid |= CryptographicOperations.FixedTimeEquals(expectedDigest, receivedDigest);
        }

        return isValid;
    }
}
