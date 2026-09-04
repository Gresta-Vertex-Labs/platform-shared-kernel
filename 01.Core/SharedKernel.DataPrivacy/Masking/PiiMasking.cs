using System.Text;

namespace SharedKernel.DataPrivacy.Masking;

/// <summary>
/// Pure, allocation-minimal, deterministic helpers for masking personally identifiable or
/// otherwise sensitive string values before they reach a log line, an audit trail, or any other
/// destination that should never see the full value.
/// </summary>
/// <remarks>
/// Every member is null/empty-safe and NEVER THROWS — a masking helper that can throw is a
/// liability at exactly the call site whose whole purpose is to keep sensitive data out of an
/// exception message or a crash dump. No member of this class uses reflection anywhere, directly
/// or indirectly.
/// </remarks>
public static class PiiMasking
{
    /// <summary>
    /// The fixed sentinel <see cref="Suppress"/> always returns, regardless of input.
    /// </summary>
    public const string RedactedSentinel = "[REDACTED]";

    private const string EmailLocalMask = "***";

    /// <summary>
    /// Masks an email address: the local part (before <c>@</c>) reveals at most its first
    /// character, and the domain is preserved in full.
    /// </summary>
    /// <param name="email">The email address to mask. May be <see langword="null"/> or empty.</param>
    /// <returns>
    /// <para><see langword="null"/> or whitespace-only input → <see cref="string.Empty"/>.</para>
    /// <para>
    /// A local part longer than one character reveals its first character followed by a fixed
    /// three-asterisk mask — e.g. <c>"j.doe@example.com"</c> → <c>"j***@example.com"</c>. The mask
    /// is always exactly three characters, never proportional to the real local part's length, so
    /// the output never discloses how long the local part actually was.
    /// </para>
    /// <para>
    /// A local part of zero or one character is masked in full with no character revealed — e.g.
    /// <c>"a@x.com"</c> → <c>"***@x.com"</c>. Revealing the single character of a one-character
    /// local part would disclose the entire local part, which the "reveal only the first
    /// character" rule above must never do.
    /// </para>
    /// <para>
    /// No <c>@</c> anywhere in the input means the value is not email-shaped: the whole string is
    /// masked using the same local-part rule described above, with no <c>@domain</c> suffix
    /// appended (there is no domain segment to preserve). When more than one <c>@</c> is present,
    /// the LAST one is treated as the local/domain boundary — the trailing segment after the final
    /// <c>@</c> is the most domain-like candidate available.
    /// </para>
    /// </returns>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        int atIndex = email.LastIndexOf('@');
        if (atIndex < 0)
        {
            return MaskLocalPart(email);
        }

        string local = email[..atIndex];
        string domain = email[(atIndex + 1)..];

        return $"{MaskLocalPart(local)}@{domain}";
    }

    /// <summary>
    /// Masks a phone number, preserving the trailing 2–4 digits (whichever is safe given the
    /// total digit count present) and every non-digit character (<c>+</c>, spaces, dashes,
    /// parentheses) in its original position; every other digit is replaced with <c>*</c>.
    /// </summary>
    /// <param name="phoneNumber">The phone number to mask. May be <see langword="null"/> or empty.</param>
    /// <returns>
    /// <para><see langword="null"/> or whitespace-only input → <see cref="string.Empty"/>.</para>
    /// <para>
    /// Only Unicode decimal-digit characters (<see cref="char.IsDigit(char)"/>) count toward "last
    /// N digits" and are ever masked. Separator characters keep their original position in the
    /// output and are never counted, never masked, and never removed — e.g.
    /// <c>"+1 (555) 123-4567"</c> masks to <c>"+* (***) ***-4567"</c>.
    /// </para>
    /// <para>
    /// The number of trailing digits kept is chosen deliberately from the total digit count, so a
    /// short number is never over-revealed proportionally:
    /// </para>
    /// <list type="bullet">
    /// <item><description>4 or more digits total → keep the last 4.</description></item>
    /// <item><description>2 or 3 digits total → keep the last 2.</description></item>
    /// <item><description>Fewer than 2 digits total → keep none (every digit is masked).</description></item>
    /// </list>
    /// </returns>
    public static string Phone(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        int totalDigits = CountDigits(phoneNumber);
        int keepCount = totalDigits switch
        {
            >= 4 => 4,
            >= 2 => 2,
            _ => 0,
        };

        return MaskAllButTrailingDigits(phoneNumber, totalDigits, keepCount);
    }

    /// <summary>
    /// Masks a payment-card PAN, preserving only the trailing 4 digits and every non-digit
    /// separator character (spaces, dashes) in its original position; every other digit is
    /// replaced with <c>*</c>.
    /// </summary>
    /// <param name="cardNumber">The PAN to mask. May be <see langword="null"/> or empty.</param>
    /// <returns>
    /// <para><see langword="null"/> or whitespace-only input → <see cref="string.Empty"/>.</para>
    /// <para>
    /// Only Unicode decimal-digit characters (<see cref="char.IsDigit(char)"/>) count toward "last
    /// 4 digits" and are ever masked; separators (spaces, dashes) keep their original position and
    /// are never counted, masked, or removed — e.g. <c>"4111-1111-1111-1111"</c> masks to
    /// <c>"****-****-****-1111"</c>, and a 19-digit PAN masks identically (only the trailing 4
    /// digits of however many are present stay visible).
    /// </para>
    /// <para>
    /// Unlike <see cref="Phone"/>, this reveal window is a FIXED "last 4", never a range. If the
    /// input contains fewer than 4 digits it is not a real PAN; every digit present is retained
    /// rather than silently narrowing to a shorter reveal window the way <see cref="Phone"/> does
    /// for a short number — a deliberate difference, not an oversight, since a PAN this short
    /// carries no meaningful "hide behind" length to protect in the first place.
    /// </para>
    /// </returns>
    public static string Pan(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return string.Empty;
        }

        int totalDigits = CountDigits(cardNumber);
        int keepCount = Math.Min(totalDigits, 4);

        return MaskAllButTrailingDigits(cardNumber, totalDigits, keepCount);
    }

    /// <summary>
    /// Returns a fixed sentinel value regardless of input — for fields with no safe partial
    /// reveal at all (e.g. a national ID number, a security-question answer, or a stored
    /// credential).
    /// </summary>
    /// <param name="value">
    /// Ignored. Present only so every <see cref="PiiMasking"/> member shares the same
    /// <c>string? → string</c> call shape, letting a caller swap masking strategies for a field
    /// without changing the call site.
    /// </param>
    /// <returns>
    /// The fixed <see cref="RedactedSentinel"/> value, always — including for
    /// <see langword="null"/> or empty input. This is the whole point of <see cref="Suppress"/>:
    /// unlike <see cref="Email"/>/<see cref="Phone"/>/<see cref="Pan"/>, its output must never vary
    /// with — and therefore never leak anything about — the real value.
    /// </returns>
    public static string Suppress(string? value) => RedactedSentinel;

    private static string MaskLocalPart(string local) =>
        local.Length > 1 ? $"{local[0]}{EmailLocalMask}" : EmailLocalMask;

    private static int CountDigits(string value)
    {
        int count = 0;
        foreach (char c in value)
        {
            if (char.IsDigit(c))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Replaces every digit character in <paramref name="value"/> with <c>*</c> except the last
    /// <paramref name="keepCount"/> digits (counted among digit characters only); every non-digit
    /// character is copied through unchanged, in its original position.
    /// </summary>
    private static string MaskAllButTrailingDigits(string value, int totalDigits, int keepCount)
    {
        int maskBeforeDigitIndex = totalDigits - keepCount;
        var builder = new StringBuilder(value.Length);
        int digitIndex = 0;

        foreach (char c in value)
        {
            if (!char.IsDigit(c))
            {
                builder.Append(c);
                continue;
            }

            builder.Append(digitIndex >= maskBeforeDigitIndex ? c : '*');
            digitIndex++;
        }

        return builder.ToString();
    }
}
