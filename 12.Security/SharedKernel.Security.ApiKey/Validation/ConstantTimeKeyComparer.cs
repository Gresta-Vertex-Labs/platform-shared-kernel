using System.Text;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Security.ApiKey.Validation;

// Constant-time equality comparison for two presented-credential strings, built on
// SharedKernel.Cryptography's IHmacSigner — never string.Equals/==/SequenceEqual on secret-derived
// content (timing-attack surface). Used by ApiKeyAuthenticationHandler to detect a mismatch between a
// header-supplied and query-supplied credential on the same request without leaking timing information
// about which of the two values is "more correct".
//
// Technique: HMAC is a pseudorandom function, so HMAC_pepper(a) == HMAC_pepper(b) iff a == b (with
// overwhelming, cryptographically negligible probability of collision otherwise) — IHmacSigner.Verify
// already performs its own internal comparison via CryptographicOperations.FixedTimeEquals, so this never
// needs a raw byte-array comparison of its own.
internal static class ConstantTimeKeyComparer
{
    private static readonly byte[] ComparisonPepper =
        Encoding.UTF8.GetBytes("SharedKernel.Security.ApiKey.ConstantTimeKeyComparer");

    internal static bool AreEqual(IHmacSigner hmacSigner, string left, string right)
    {
        ArgumentNullException.ThrowIfNull(hmacSigner);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        var leftSignature = hmacSigner.Sign(leftBytes, ComparisonPepper);
        return hmacSigner.Verify(rightBytes, leftSignature, ComparisonPepper);
    }
}
