using System.Text;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Security.Mtls.Validation;

// Constant-time equality comparison for two certificate-thumbprint strings, built on
// SharedKernel.Cryptography's IHmacSigner — never string.Equals/==/SequenceEqual (timing-attack
// surface). Mirrors SharedKernel.Security.ApiKey.Validation.ConstantTimeKeyComparer's exact technique:
// HMAC is a pseudorandom function, so HMAC_pepper(a) == HMAC_pepper(b) iff a == b (with overwhelming,
// cryptographically negligible probability of collision otherwise) — IHmacSigner.Verify already performs
// its own internal constant-time comparison via CryptographicOperations.FixedTimeEquals.
internal static class ConstantTimeThumbprintComparer
{
    private static readonly byte[] ComparisonPepper =
        Encoding.UTF8.GetBytes("SharedKernel.Security.Mtls.ConstantTimeThumbprintComparer");

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
