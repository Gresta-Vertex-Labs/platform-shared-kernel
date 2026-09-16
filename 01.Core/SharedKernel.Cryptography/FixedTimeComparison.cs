using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Cryptography;

/// <summary>
/// Compares secret values in time that does not depend on where they first differ, so an attacker cannot recover a
/// secret by timing repeated guesses.
/// </summary>
/// <remarks>
/// Use this for API keys, tokens, one-time codes, signatures, thumbprints and any other value an attacker can guess
/// at. Never compare them with <c>==</c>, <see cref="string.Equals(string?, string?)"/> or
/// <c>SequenceEqual</c>, which return as soon as they find a difference.
/// </remarks>
public static class FixedTimeComparison
{
    /// <summary>
    /// Returns whether two byte sequences are equal. The time taken depends only on the lengths, never on the
    /// contents.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when both sequences have the same length and the same bytes.</returns>
    /// <remarks>
    /// Different lengths return <see langword="false"/> immediately, which reveals that the lengths differ. Use
    /// <see cref="AreEqual(string, string)"/> when the length itself must stay hidden.
    /// </remarks>
    public static bool AreEqual(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        CryptographicOperations.FixedTimeEquals(left, right);

    /// <summary>
    /// Returns whether two strings are equal, comparing their UTF-8 SHA-256 digests so that neither the position of
    /// the first difference nor the length of either string affects the time taken.
    /// </summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the strings are ordinally equal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="left"/> or <paramref name="right"/> is <see langword="null"/>.</exception>
    public static bool AreEqual(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Span<byte> leftDigest = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> rightDigest = stackalloc byte[SHA256.HashSizeInBytes];
        HashUtf8(left, leftDigest);
        HashUtf8(right, rightDigest);

        // Two distinct strings with the same SHA-256 digest would compare equal; no such pair is known.
        return CryptographicOperations.FixedTimeEquals(leftDigest, rightDigest);
    }

    /// <summary>
    /// Returns whether <paramref name="candidate"/> equals any of <paramref name="expectedValues"/>. Every expected
    /// value is compared, whichever one matches, so the time taken does not reveal which value matched.
    /// </summary>
    /// <param name="candidate">The value presented by the caller.</param>
    /// <param name="expectedValues">
    /// The accepted values, for example the current and previous key during a rotation window.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> equals at least one expected value.</returns>
    /// <exception cref="ArgumentNullException">An argument or an expected value is <see langword="null"/>.</exception>
    public static bool AreEqualToAny(string candidate, IEnumerable<string> expectedValues)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(expectedValues);

        bool matched = false;
        foreach (string expected in expectedValues)
        {
            ArgumentNullException.ThrowIfNull(expected, nameof(expectedValues));
            matched |= AreEqual(candidate, expected);
        }

        return matched;
    }

    private static void HashUtf8(string value, Span<byte> digest)
    {
        int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        Span<byte> buffer = maxBytes <= 256 ? stackalloc byte[256] : new byte[maxBytes];
        try
        {
            int written = Encoding.UTF8.GetBytes(value, buffer);
            SHA256.HashData(buffer[..written], digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
